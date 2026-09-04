# Move GitHub Search to Azure SWA Managed Function

## Problem

The watchlist search is slow. Every keystroke (after 300ms debounce) sends a request through the .NET backend API, which proxies to GitHub's Search API. The round-trip is:

```
Client → .NET API (Azure App Service) → GitHub API → .NET API → Client
```

The backend endpoint (`GitHubSearchRoutes.cs`) is a pure passthrough — validate input, call GitHub, map 4 fields. There's no server-side caching, so identical queries from different users each hit GitHub. GitHub's Search API has ~300-500ms latency per call, and the extra hop through the .NET backend adds more.

## Proposed Solution

Add an Azure Functions managed API to the SWA that handles GitHub search directly, with an in-memory response cache. This eliminates the .NET backend from the search path entirely.

New flow:

```
Client → SWA managed function /api/github-search (cache hit? return)
                                                  (cache miss? → GitHub API → cache + return)
```

## Architecture

### New: `patchnotes-web/api/` — SWA Managed Function

A single Azure Function (Node.js/TypeScript, v4 programming model) that:

1. Accepts `GET /api/github-search?q={query}`
2. Validates query (min 2 chars)
3. Checks module-level in-memory cache (keyed by normalized query, 5-minute TTL)
4. On miss: calls `https://api.github.com/search/repositories` with server-side GitHub PAT
5. Caches and returns the response

### File Structure

```
patchnotes-web/
├── api/
│   ├── src/
│   │   └── functions/
│   │       └── github-search.ts    # The search endpoint
│   ├── host.json
│   ├── local.settings.json         # Local dev secrets (gitignored)
│   ├── package.json
│   └── tsconfig.json
├── public/
│   └── staticwebapp.config.json    # Add apiRuntime
├── dist/                           # Existing Vite build output
└── ...
```

### Function Implementation (`api/src/functions/github-search.ts`)

```typescript
import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";

// Module-level cache — persists across invocations on the same warm instance
const cache = new Map<string, { data: unknown; expiry: number }>();
const CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes

app.http("github-search", {
  methods: ["GET"],
  authLevel: "anonymous",
  route: "github-search",
  handler: async (request: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> => {
    const query = request.query.get("q")?.trim();

    if (!query || query.length < 2) {
      return {
        status: 400,
        jsonBody: { error: "Query parameter 'q' is required and must be at least 2 characters" },
      };
    }

    const cacheKey = query.toLowerCase();
    const cached = cache.get(cacheKey);
    if (cached && cached.expiry > Date.now()) {
      return { jsonBody: cached.data };
    }

    const githubToken = process.env.GITHUB_PAT;
    const headers: Record<string, string> = {
      Accept: "application/vnd.github.v3+json",
      "User-Agent": "PatchNotes",
    };
    if (githubToken) {
      headers.Authorization = `Bearer ${githubToken}`;
    }

    const url = `https://api.github.com/search/repositories?q=${encodeURIComponent(query)}&per_page=10`;
    const response = await fetch(url, { headers });

    if (!response.ok) {
      context.error(`GitHub API returned ${response.status}`);
      return { status: 502, jsonBody: { error: "GitHub search failed" } };
    }

    const json = (await response.json()) as {
      items?: Array<{
        owner: { login: string };
        name: string;
        description: string | null;
        stargazers_count: number;
      }>;
    };

    const results = (json.items ?? []).map((r) => ({
      owner: r.owner.login,
      repo: r.name,
      description: r.description,
      starCount: r.stargazers_count,
    }));

    cache.set(cacheKey, { data: results, expiry: Date.now() + CACHE_TTL_MS });

    return { jsonBody: results };
  },
});
```

### Configuration Changes

**`patchnotes-web/public/staticwebapp.config.json`** — add API runtime:

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/assets/*", "/api/*"]
  },
  "platform": {
    "apiRuntime": "node:20"
  }
}
```

**`.github/workflows/deploy.yml`** — point `api_location` to the new folder:

```yaml
api_location: patchnotes-web/api
```

**Azure Portal / CLI** — add `GITHUB_PAT` app setting:

```bash
az staticwebapp appsettings set --name <app-name> --setting-names "GITHUB_PAT=ghp_..."
```

## Frontend Changes

### Update `useGithubSearch` hook

Change the hook in `patchnotes-web/src/api/hooks.ts` to call the SWA API route directly instead of using the Orval-generated client:

```typescript
export function useGithubSearch(query: string) {
  return useQuery({
    queryKey: ["github-search", query],
    queryFn: async ({ signal }) => {
      const res = await fetch(`/api/github-search?q=${encodeURIComponent(query)}`, { signal });
      if (!res.ok) throw new Error("GitHub search failed");
      return res.json() as Promise<
        Array<{
          owner: string;
          repo: string;
          description: string | null;
          starCount: number;
        }>
      >;
    },
    enabled: query.length >= 2,
    staleTime: 60_000,
  });
}
```

This bypasses the .NET backend entirely. The `/api/github-search` path routes to the SWA managed function automatically (same origin, no CORS needed).

## Cleanup

Once the SWA function is live:

1. **Delete** `PatchNotes.Api/Routes/GitHubSearchRoutes.cs`
2. **Remove** `app.MapGitHubSearchRoutes()` call from `Program.cs`
3. **Remove** the Orval-generated client in `patchnotes-web/src/api/generated/git-hub-search/`
4. **Remove** the GitHub search endpoint from the OpenAPI spec / Orval config
5. **Remove** the `GitHubRepoSearchResultDto` if unused elsewhere

## Tradeoffs & Considerations

### Cold Starts

SWA managed functions run on the Consumption plan. Cold starts can be 15-30 seconds on the first request after idle. Mitigations:

- The frontend already has loading states and the 60s React Query staleTime helps
- Consider increasing `staleTime` to 5 minutes to match the server cache TTL
- For most users, the function will be warm since others will have already triggered it

### Cache Scope

The in-memory cache is per-instance. If SWA scales to multiple instances, each has its own cache. This is fine — the cache is an optimization, not a correctness requirement. Even a single cache hit saves a GitHub API call.

### Auth

The current .NET endpoint has a `requireAuth` filter. The SWA managed function doesn't replicate this. Since GitHub search results are public data and the function doesn't expose any user-specific information, this is acceptable. If auth is desired, SWA has built-in auth integration via `/.auth/` routes that could be checked.

### Rate Limits

- **With PAT:** 30 search requests/minute to GitHub (shared across all users hitting the same instance)
- **Without PAT:** 10 requests/minute per IP
- The 5-minute cache means popular queries like "react" only hit GitHub once per 5 minutes per instance, dramatically reducing rate limit pressure

## Implementation Steps

1. Create `patchnotes-web/api/` with `package.json`, `tsconfig.json`, `host.json`, `local.settings.json`
2. Implement `github-search` function
3. Update `staticwebapp.config.json` with `apiRuntime` and `/api/*` exclusion
4. Update `useGithubSearch` hook to call `/api/github-search`
5. Test locally with `swa start` (Azure SWA CLI)
6. Update deploy workflow (`api_location: patchnotes-web/api`)
7. Set `GITHUB_PAT` in Azure app settings
8. Deploy, verify, then clean up the .NET endpoint and generated client
