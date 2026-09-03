# Performance Improvements

Audit date: 2026-02-18

## Backend (API)

### High Priority

#### 1. Cache the default feed response

The default (anonymous) feed runs an expensive `GROUP BY` over the entire Releases table on every request (`FeedRoutes.cs:36`). The result — top-10 most recently active packages — changes only when new releases are synced (a few times per day at most).

**Fix:** Add `IMemoryCache` with a 5-10 minute TTL for the default feed response. Register `builder.Services.AddMemoryCache()` in `Program.cs` and cache the full `FeedResponseDto` for anonymous users. Invalidate on sync completion if needed, but stale-by-a-few-minutes is acceptable.

```csharp
// Program.cs
builder.Services.AddMemoryCache();

// FeedRoutes.cs
if (isDefaultFeed && cache.TryGetValue("default-feed", out FeedResponseDto cached))
    return Results.Ok(cached);

// ... build response ...

if (isDefaultFeed)
    cache.Set("default-feed", response, TimeSpan.FromMinutes(5));
```

#### 2. Add `(PackageId, PublishedAt)` composite index

Nearly every release query filters by `PackageId` and sorts by `PublishedAt`. The existing indexes cover the filter but not the sort, forcing a post-index sort step.

**Fix:** Add to `PatchNotesDbContext.OnModelCreating`:

```csharp
entity.HasIndex(e => new { e.PackageId, e.PublishedAt });
```

This also benefits the feed's `GROUP BY` query.

### Medium-High Priority

#### 3. Collapse user lookup into a single query

Every authenticated endpoint does two sequential queries — find user by `StytchUserId`, then query by `user.Id`. This can be a single JOIN.

**Fix** in `RouteUtils.GetAuthenticatedUserWatchlistIds`:

```csharp
// Before: 2 queries
var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == session.UserId);
var ids = await db.Watchlists.Where(w => w.UserId == user.Id).Select(w => w.PackageId).ToListAsync();

// After: 1 query
return await db.Watchlists
    .Where(w => w.User.StytchUserId == session.UserId)
    .Select(w => w.PackageId)
    .ToListAsync();
```

`Users.StytchUserId` has a unique index, so the JOIN is efficient.

### Medium Priority

#### 4. Add `.AsNoTracking()` to all read-only endpoints

None of the GET endpoints use `.AsNoTracking()`. EF change tracking adds memory and CPU overhead for every materialized entity.

**Affected endpoints:** `GET /api/feed`, `GET /api/releases`, `GET /api/packages`, `GET /api/watchlist`, `GET /api/packages/{id}/releases`, `GET /api/packages/{id}/summaries`, `GET /api/summaries`, `GET /sitemap.xml`

**Fix:** Mechanical — add `.AsNoTracking()` after every `db.Releases`, `db.Packages`, etc. on read-only queries.

#### 5. Remove redundant `.Include()` calls

`ReleaseRoutes.cs:60`, `SummaryRoutes.cs:26`, `SummaryRoutes.cs:69` all have `.Include()` followed by a `.Select()` projection that accesses the same navigation property. EF Core generates the JOIN from the projection; the `.Include()` is redundant and causes unnecessary change tracking.

#### 6. Feed summary query over-fetches

`FeedRoutes.cs:128` fetches summaries for all `(PackageId, MajorVersion, IsPrerelease)` combinations when it only needs the ones that survived filtering. Filter the query to match exactly the surviving group keys.

#### 7. Bulk package insert — batch the save

`PackageRoutes.cs:485` calls `SaveChangesAsync()` inside a loop, producing N round-trips. Move it outside the loop and batch the existence check upfront.

---

## Frontend

### High Priority

#### 8. Add route-level code splitting

No routes are lazy-loaded — all page components are statically imported in `src/routes/`. TanStack Router supports lazy route components via `createLazyFileRoute`.

**Fix:** Convert heavy/infrequently-visited routes to lazy loading:

```tsx
// routes/admin.tsx — before
import { Admin } from "../pages/Admin";
export const Route = createFileRoute("/admin")({ component: Admin });

// routes/admin.tsx — after
import { createFileRoute, createLazyFileRoute } from "@tanstack/react-router";
export const Route = createLazyFileRoute("/admin")({
  component: () => import("../pages/Admin").then((m) => m.Admin),
});
```

**Candidates for lazy loading:** `/admin`, `/admin/emails`, `/releases/$id`, `/packages/$owner/$repo` (all carry heavy deps or are rarely the entry point).

**Keep eagerly loaded:** `/` (home) and `/watchlist` (most common entry points).

#### 9. Lazy-load `react-markdown`

`react-markdown` pulls in ~100KB of parser/renderer code. It's eagerly imported in `HomePage.tsx:4`, `PackageDetailByRepo.tsx:3`, and `ReleaseDetail.tsx:2`, landing in the initial bundle regardless of which page loads first.

**Fix:** Lazy-load with `React.lazy`:

```tsx
const Markdown = lazy(() => import('react-markdown'))

// In render:
<Suspense fallback={<span>{summary}</span>}>
  <Markdown>{summary}</Markdown>
</Suspense>
```

#### 10. Fix `invalidateQueries` key mismatch in AdminEmails

`AdminEmails.tsx:177` uses a hand-written query key `['/api/admin/email-templates']`, but invalidation on line 9 uses the generated `getGetEmailTemplatesQueryKey()`. These keys don't match, so cache is never invalidated after saving a template — users see stale data. This is a correctness bug, not just performance.

### Medium Priority

#### 11. Memoize `sortedGroups` / `groupedByPackageMap` in HomePage

`HomePage.tsx:433-451` — these derived values are recomputed on every render. Wrap in `useMemo` keyed on `[versionGroups, sortBy]`.

#### 12. Prevent full-list re-renders on expand/collapse

`HomePage.tsx:579-600` — toggling any `SummaryCard` creates a new `expandedGroups` Set, re-rendering every card. Wrap `SummaryCard` in `React.memo` and stabilize `onToggle` with `useCallback`.

#### 13. Deduplicate `checkSubscription` call

Both `HomePage.tsx:406` and `UserMenu.tsx:263` call `checkSubscription()` on mount. This fires two simultaneous `GET /api/subscription/status` requests. Remove the one in `HomePage` (UserMenu already handles it), or convert to a TanStack Query for automatic deduplication.

---

## Completed

### Sync releases on package creation (PR #369)

New packages added via `POST /api/watchlist/github/{owner}/{repo}` previously had zero releases until the next 6-hour timer sync. Users saw an empty package.

**Fix:** Added an HTTP-triggered Azure Function (`SyncNewPackages`) that syncs packages where `LastFetchedAt == null`. The API fires a best-effort POST to this function after creating a new package. Duplicate pings are harmless — the function queries the DB for never-synced packages each time, and `LastFetchedAt` gets set after sync. The 6-hour timer remains as a safety net.

---

## Architectural Suggestions

### 1. Request-scoped auth context

Currently, Stytch session validation is called inline in every endpoint via `RouteUtils`. If multiple services in a request pipeline need the authenticated user, the Stytch call fires multiple times.

**Suggestion:** Create a lightweight middleware or request-scoped service that validates the session once, caches the result in `HttpContext.Items`, and exposes it via an `ICurrentUser` service. This eliminates redundant Stytch calls and centralizes auth logic:

```csharp
public class CurrentUserMiddleware
{
    public async Task InvokeAsync(HttpContext context, IStytchClient stytch)
    {
        var token = context.Request.Cookies["stytch_session"];
        if (!string.IsNullOrEmpty(token))
        {
            var session = await stytch.AuthenticateSessionAsync(token);
            context.Items["StytchSession"] = session;
        }
        await _next(context);
    }
}
```

Endpoints then inject `ICurrentUser` or read from `HttpContext.Items` — zero Stytch calls after the first.

### 2. Response caching layer for public endpoints

The feed, packages list, and sitemap are public, read-heavy, and change infrequently. Rather than adding `IMemoryCache` calls to each endpoint individually, consider a thin caching middleware or response caching:

```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder.NoCache());
    options.AddPolicy("PublicShort", builder =>
        builder.Expire(TimeSpan.FromMinutes(5)).Tag("public"));
});

// On endpoints:
group.MapGet("/", handler).CacheOutput("PublicShort");
```

This also sets proper `Cache-Control` headers for CDN/browser caching. Invalidate with `cache.EvictByTagAsync("public")` when sync completes.

### 3. Convert subscription check to TanStack Query

The Zustand-based `checkSubscription` is a raw fetch with no caching, deduplication, or stale-while-revalidate. Converting it to a TanStack Query would:

- Deduplicate simultaneous calls automatically
- Cache the result with `staleTime` / `gcTime`
- Enable background refetch on window focus
- Remove the manual `useEffect` + store pattern

```tsx
export function useSubscriptionStatus() {
  const { user } = useStytchUser();
  return useQuery({
    queryKey: ["/api/subscription/status"],
    queryFn: () => api.get("/subscription/status"),
    enabled: !!user,
    staleTime: 5 * 60 * 1000,
  });
}
```

### 4. Global `gcTime` for TanStack Query

The current `QueryClient` sets `staleTime: 60s` but leaves `gcTime` at the default 5 minutes. For a single-page app where users navigate between feed/watchlist/detail frequently within a session, bumping `gcTime` to 15-30 minutes keeps cached data alive across navigations without holding memory indefinitely.

### 5. Consider `DbContext` pooling

EF Core's `AddDbContextPool` reuses `DbContext` instances instead of creating/disposing one per request. For a high-throughput API this reduces GC pressure and allocation overhead:

```csharp
// Instead of AddDbContext:
services.AddDbContextPool<PatchNotesDbContext, SqlServerContext>(options =>
    ConfigureDbContext(options, connectionString), poolSize: 128);
```

Requires that endpoints don't store state on the `DbContext` between requests (already true since all endpoints are stateless request handlers).
