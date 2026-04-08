# Admin CLI for PatchNotes

> A command-line tool for managing the PatchNotes application, usable by humans and AI agents.

## Motivation

Admin operations currently require either:

- The browser (Stytch session + admin role) to hit `/api/admin/*` endpoints
- Direct database access via `PatchNotes.Sync` for sync operations

Neither works well for quick operational tasks or for AI agents that need to perform admin work in automated pipelines. A CLI tool bridges this gap.

## Goals

- Expose existing admin operations as CLI commands
- Provide a single tool for package, sync, and user management
- Support both interactive and scriptable (non-interactive, JSON output) usage
- Handle authentication in a way that works for humans, CI, and AI agents
- Avoid introducing security holes in the process

## Non-Goals

- Replacing the web UI for end users
- Building a general-purpose API client
- Adding new admin capabilities (the CLI wraps what already exists)

## Authentication

This is the area that needs the most care. The current auth model is browser-centric:

- **Web users**: Stytch session cookie, validated on every request
- **Admin users**: Stytch `patch_notes_admin` role, checked by `RouteUtils.CreateAdminFilter()`
- **Sync CLI**: No auth at all (direct DB access, runs on trusted infrastructure)
- **Webhooks**: Signature verification (Stripe/Stytch secrets)

None of these work well for a CLI tool. Use **Stytch M2M** (machine-to-machine) authentication to keep all auth in one system and avoid building custom key infrastructure.

### How Stytch M2M works

Stytch M2M uses the standard OAuth 2.0 client_credentials flow:

1. **Create an M2M client** in the Stytch dashboard (or via API). Each client gets a `client_id` and `client_secret`. The secret is shown once at creation time.
2. **Obtain a token**: The CLI exchanges `client_id` + `client_secret` for a short-lived JWT (1 hour default) via Stytch's public token endpoint.
3. **Send the token**: CLI sends `Authorization: Bearer <jwt>` on API requests.
4. **Validate server-side**: The API validates the JWT locally against Stytch's JWKS. No Stytch API call needed for validation — it's standard JWT verification.

```
CLI                         Stytch                      API
 |                            |                          |
 |-- client_credentials ----->|                          |
 |<-------- JWT (1hr) -------|                          |
 |                            |                          |
 |------------- Bearer JWT --------------------------->|
 |                            |       validate JWT      |
 |                            |       via JWKS (local)  |
 |<--------------------------------------------- 200 --|
```

### Scopes

Stytch M2M has native scope support. Scopes are assigned when creating the M2M client and embedded in the JWT `scope` claim.

Two scopes:

- `admin:read` — all GET endpoints under `/api/admin/*`
- `admin:write` — all mutating endpoints under `/api/admin/*` (POST, PATCH, PUT, DELETE)

Scope-to-endpoint mapping:

| Scope | Endpoints |
|---|---|
| `admin:read` | `GET /api/admin/packages/health`, `GET /api/admin/users`, `GET /api/admin/users/{id}`, `GET /api/admin/releases`, `GET /api/admin/summaries`, `GET /api/admin/digest-emails`, `GET /api/admin/webhook-events`, `GET /api/admin/email-templates`, `GET /api/admin/email-templates/{id}` |
| `admin:write` | All POST/PATCH/PUT/DELETE under `/api/admin/*` (reset-sync, disable-sync, trigger-sync, reset-summaries, reset-releases, summaries/regenerate-all, sync/trigger-all, watchlist-template CRUD, email-template update/test) |

An M2M client with `admin:write` implicitly has `admin:read` — the admin filter checks `admin:write` for mutating methods and `admin:read` for GET.

M2M auth is **not** accepted on non-admin routes. User, watchlist, subscription, and feed endpoints remain session-cookie-only. See the `CreateM2MAuthFilter()` section below.

### M2M client setup

Create M2M clients for each use case:

| Client | Scopes | Purpose |
|---|---|---|
| `paul-cli` | `admin:read admin:write` | Personal admin CLI |
| `logwatcher-agent` | `admin:read admin:write` | LogWatcher issue filing |
| `ci-readonly` | `admin:read` | CI health checks |

Create via Stytch dashboard or API. The `client_secret` is returned once — store it securely.

### Server-side changes

The API currently resolves authenticated browser requests in `RouteUtils.CreateAuthFilter()` by validating the `stytch_session` cookie via a Stytch API call on every request. Add JWT Bearer validation as a second auth path for admin route groups via a new `CreateM2MAuthFilter()`. The JWT is validated locally against Stytch's JWKS — no Stytch API call needed per request.

#### 1. Add JWT Bearer authentication in Program.cs

Use the framework's built-in JWKS URI support. This handles key caching, background refresh, and rotation automatically — no manual one-shot fetch.

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var stytchProjectId = builder.Configuration["Stytch:ProjectId"]!;
var stytchDomain = stytchProjectId.StartsWith("project-live")
    ? "api.stytch.com"
    : "test.stytch.com";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://{stytchDomain}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"stytch.com/{stytchProjectId}",
            ValidateAudience = true,
            ValidAudience = stytchProjectId,
            ValidateLifetime = true,
            ValidAlgorithms = new[] { "RS256" },
            ClockSkew = TimeSpan.FromMinutes(2),
        };
        options.MetadataAddress =
            $"https://{stytchDomain}/v1/sessions/jwks/{stytchProjectId}";
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                logger.LogWarning(context.Exception,
                    "Stytch JWT authentication failed");
                return Task.CompletedTask;
            }
        };
    });
```

Note: the JWKS URL path for B2C is `/v1/sessions/jwks/{projectId}` (not `/v1/b2b/sessions/jwks/`).

Also add a CSRF bypass for Bearer-authenticated requests in `CsrfMiddleware`. Machine clients don't send `Origin` or `Sec-Fetch-Site` headers:

```csharp
// In CsrfMiddleware.InvokeAsync, after the webhook bypass:
var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
if (authHeader?.StartsWith("Bearer ") == true)
{
    await _next(context);
    return;
}
```

This is safe because Bearer tokens are not automatically attached by browsers (unlike cookies), so CSRF protection is not needed for token-authenticated requests.

#### 2. Add CreateM2MAuthFilter() for admin routes only

Do **not** modify the existing `CreateAuthFilter()`. Instead, add a separate filter that accepts M2M JWTs. This filter is only applied to `/api/admin/*` routes, so user, watchlist, and subscription endpoints remain session-cookie-only.

```csharp
/// <summary>
/// Auth filter that accepts either a Stytch session cookie (existing)
/// or an M2M Bearer JWT. Only apply to admin route groups.
/// </summary>
public static Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate>
    CreateM2MAuthFilter()
{
    return (context, next) => async invocationContext =>
    {
        var httpContext = invocationContext.HttpContext;

        // Path 1: M2M JWT via Authorization header
        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
        {
            var result = await httpContext.AuthenticateAsync(
                JwtBearerDefaults.AuthenticationScheme);
            if (result.Succeeded)
            {
                var sub = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var scope = result.Principal.FindFirstValue("scope") ?? "";
                httpContext.Items["M2MClientId"] = sub;
                httpContext.Items["M2MScopes"] = scope.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries);
                httpContext.Items["AuthMethod"] = "m2m";
                return await next(invocationContext);
            }
            return Results.Unauthorized();
        }

        // Path 2: fall through to Stytch session cookie (existing behavior)
        var stytchClient = httpContext.RequestServices
            .GetRequiredService<IStytchClient>();
        var sessionToken = httpContext.Request.Cookies["stytch_session"];
        if (string.IsNullOrEmpty(sessionToken))
            return Results.Unauthorized();

        var session = await stytchClient.AuthenticateSessionAsync(
            sessionToken, httpContext.RequestAborted);
        if (session == null)
            return Results.Unauthorized();

        httpContext.Items["StytchUserId"] = session.UserId;
        httpContext.Items["StytchSessionId"] = session.SessionId;
        httpContext.Items["StytchEmail"] = session.Email;
        httpContext.Items["StytchSession"] = session;
        httpContext.Items["AuthMethod"] = "session";

        return await next(invocationContext);
    };
}
```

Admin route groups use `CreateM2MAuthFilter()` instead of `CreateAuthFilter()`:

```csharp
// In PackageRoutes.cs (and other admin route files):
var adminPackages = app.MapGroup("/api/admin/packages")
    .AddEndpointFilterFactory(RouteUtils.CreateM2MAuthFilter())
    .AddEndpointFilterFactory(RouteUtils.CreateAdminFilter());
```

Non-admin authenticated routes (`/api/users/me`, `/api/watchlist`, `/api/subscription/*`) continue using the existing `CreateAuthFilter()` unchanged — they only accept session cookies.

#### 3. Update CreateAdminFilter() for scope checking

```csharp
public static Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate>
    CreateAdminFilter()
{
    return (context, next) => async invocationContext =>
    {
        var httpContext = invocationContext.HttpContext;
        var authMethod = httpContext.Items["AuthMethod"] as string;

        if (authMethod == "m2m")
        {
            var scopes = httpContext.Items["M2MScopes"] as string[] ?? [];
            var requiredScope = IsReadOnlyRequest(httpContext)
                ? "admin:read"
                : "admin:write";

            if (!scopes.Contains(requiredScope) && !scopes.Contains("admin:write"))
                return Results.Json(
                    new ApiError($"Forbidden: missing {requiredScope} scope"),
                    statusCode: StatusCodes.Status403Forbidden);

            return await next(invocationContext);
        }

        // Existing session-based admin check
        var session = httpContext.Items["StytchSession"] as StytchSessionResult;
        if (session == null || !session.IsAdmin)
            return Results.Json(new ApiError("Forbidden"),
                statusCode: StatusCodes.Status403Forbidden);

        return await next(invocationContext);
    };
}

private static bool IsReadOnlyRequest(HttpContext ctx) =>
    HttpMethods.IsGet(ctx.Request.Method) ||
    HttpMethods.IsHead(ctx.Request.Method);
```

This checks `admin:read` for GET requests and `admin:write` for mutating requests. An `admin:write` scope implicitly grants `admin:read`.

### CLI-side token management

The CLI handles the client_credentials exchange and caches the token:

- On `auth login`: prompt for `client_id` and `client_secret`, exchange for a token, store all three in `~/.config/patchnotes/credentials.json` (600 permissions)
- On subsequent commands: use the cached token if still valid (check `exp` claim), otherwise refresh using the stored credentials
- Token lifetime is 1 hour, so most interactive sessions won't need a refresh
- For CI/agents: support `PATCHNOTES_CLIENT_ID` and `PATCHNOTES_CLIENT_SECRET` env vars. The CLI exchanges them for a token on each invocation (no caching needed in ephemeral environments).

### Trade-offs

**Accepted:**
- If Stytch is down, the CLI cannot obtain new tokens (existing cached tokens still work for up to 1 hour)
- Each token request adds ~100-200ms latency (once per session, not per command)
- M2M client management happens in the Stytch dashboard, not in the CLI itself

**Avoided:**
- No custom `ApiKey` table, no migration, no key hashing, no bootstrap problem
- No custom auth middleware — standard JWT validation only
- No credential lifecycle to build (rotation, revocation, expiry all handled by Stytch)

### Cost

Stytch free tier includes 1,000 M2M tokens per month. With token caching (1 hour lifetime), even heavy CLI usage would consume a small fraction of that. The LogWatcher at 24 runs/day = ~720 tokens/month if it refreshes every time (and it can cache too).

### Security considerations

- **Credential storage**: `~/.config/patchnotes/credentials.json` with 600 permissions. Contains `client_id`, `client_secret`, and cached token.
- **Secret rotation**: Rotate via Stytch dashboard. The CLI's `auth login` command re-prompts for new credentials.
- **Revocation**: Delete or deactivate the M2M client in Stytch. Existing tokens remain valid until expiry (max 1 hour).
- **Audit logging**: Log the M2M client ID (from JWT `sub` claim) on each API request to Application Insights.
- **Never log the `client_secret` or full JWT**. Log only the client ID for traceability.

## Admin API Surface

The CLI is an API client, so every CLI command needs a backing endpoint. This section maps all 10 database entities to admin endpoints, noting what already exists and what's new.

Design rule: the PatchNotes CLI only talks to `/api/admin/*` endpoints on the PatchNotes API. If an equivalent non-admin endpoint already exists, add a separate admin-path endpoint for CLI usage rather than calling the non-admin route directly. This keeps admin-tool usage patterns separate from browser/public/session-based flows.

### Existing admin endpoints (no changes needed)

These are already implemented and just need to accept M2M JWT auth alongside session cookies:

| Endpoint | Method | CLI command |
|---|---|---|
| `/api/admin/packages/health` | GET | `packages list`, `sync status` |
| `/api/admin/packages/{id}/reset-sync` | POST | `sync reset` |
| `/api/admin/packages/{id}/disable-sync` | POST | `sync disable` |
| `/api/admin/packages/{id}/trigger-sync` | POST | `sync trigger` |
| `/api/admin/packages/{id}/reset-summaries` | POST | `summaries reset` |
| `/api/admin/packages/{id}/reset-releases` | POST | `releases reset` |
| `/api/admin/email-templates` | GET | `email templates list` |
| `/api/admin/email-templates/{id}` | GET | `email templates show` |
| `/api/admin/email-templates/{id}` | PUT | `email templates update` |
| `/api/admin/email-templates/{id}/test` | POST | `email send-test` |

### New admin endpoints needed

#### Packages and GitHub search

Add admin-path package management endpoints for all CLI package operations. Existing non-admin routes can remain for current web/session flows, but the CLI does not call them.

| Endpoint | Method | Scope | Purpose |
|---|---|---|---|
| `GET /api/admin/packages/{id}` | GET | `admin:read` | Get package detail for admin/CLI usage |
| `POST /api/admin/packages` | POST | `admin:write` | Add a package by GitHub owner/repo |
| `PATCH /api/admin/packages/{id}` | PATCH | `admin:write` | Update package metadata/mapping |
| `DELETE /api/admin/packages/{id}` | DELETE | `admin:write` | Delete a tracked package |
| `GET /api/admin/github/search` | GET | `admin:read` | Search GitHub repositories for add flows |

Request body:

```json
{
  "githubOwner": "facebook",
  "githubRepo": "react",
  "name": "React",
  "npmName": "react",
  "tagPrefix": null
}
```

Only `githubOwner` and `githubRepo` are required. `name` defaults to `owner/repo` if omitted. `npmName` is optional (some tracked repos aren't npm packages).

`GET /api/admin/github/search` mirrors the current `/api/github/search?q=...` behavior but lives under `/api/admin/*` so it can use `CreateM2MAuthFilter()` and `admin:read`.

`GET /api/admin/packages/{id}` can reuse the same underlying query logic as the current `GET /api/packages/{id}` route, but it is a separate endpoint with a separate contract for CLI/admin usage.

#### Users

No admin user endpoints exist today. All current user endpoints are `/api/users/me` (self-service).

| Endpoint | Method | Scope | Purpose |
|---|---|---|---|
| `GET /api/admin/users` | GET | `admin:read` | List users with pagination |
| `GET /api/admin/users/{id}` | GET | `admin:read` | User detail with subscription + watchlist |

`GET /api/admin/users` response shape:

```json
{
  "items": [
    {
      "id": "abc123",
      "email": "user@example.com",
      "name": "Jane Doe",
      "isPro": true,
      "subscriptionStatus": "active",
      "subscriptionExpiresAt": "2027-01-01T00:00:00Z",
      "emailDigestEnabled": true,
      "watchlistCount": 12,
      "lastLoginAt": "2026-04-07T10:00:00Z",
      "createdAt": "2026-01-15T08:00:00Z"
    }
  ],
  "total": 42,
  "page": 1,
  "pageSize": 50
}
```

Query params: `?page=1&pageSize=50&search=<email or name>&pro=true|false&sort=createdAt|lastLoginAt`

`GET /api/admin/users/{id}` includes the full user record plus their watchlist packages and recent digest email history.

#### Releases

Read-only access already exists via public endpoints. Add an admin list with broader filters:

| Endpoint | Method | Scope | Purpose |
|---|---|---|---|
| `GET /api/admin/releases` | GET | `admin:read` | Query releases across all packages |

Query params: `?packageId=<id>&stale=true|false&prerelease=true|false&since=<datetime>&page=1&pageSize=50`

This is useful for answering "which releases are stale?" or "what was synced in the last hour?" without knowing the package ID up front.

#### Release Summaries

The public `/api/summaries` endpoint returns summaries grouped by package. Add an admin view for operational queries:

| Endpoint | Method | Scope | Purpose |
|---|---|---|---|
| `GET /api/admin/summaries` | GET | `admin:read` | Query summaries with operational metadata |
| `POST /api/admin/summaries/regenerate-all` | POST | `admin:write` | Mark all releases stale, triggering full regeneration |

`GET /api/admin/summaries` query params: `?packageId=<id>&page=1&pageSize=50`

Response includes `generatedAt`, `updatedAt`, and the count of stale releases per group — information the public endpoint doesn't expose.

`POST /api/admin/summaries/regenerate-all` marks every release as `SummaryStale = true` and deletes all `ReleaseSummary` rows. The next sync cycle picks up the regeneration work.

#### Sent Digest Emails

No endpoints exist for this table. Add read-only admin access:

| Endpoint | Method | Scope | Purpose |
|---|---|---|---|
| `GET /api/admin/digest-emails` | GET | `admin:read` | Query sent digest history |

Query params: `?userId=<id>&status=sent|failed|pending&since=<datetime>&page=1&pageSize=50`

Response shape:

```json
{
  "items": [
    {
      "id": "abc123",
      "userId": "user456",
      "recipientEmail": "user@example.com",
      "subject": "Your weekly digest",
      "status": "sent",
      "resendEmailId": "re_abc123",
      "sentAt": "2026-04-07T09:00:00Z"
    }
  ],
  "total": 120,
  "page": 1,
  "pageSize": 50
}
```

The `htmlBody` field is excluded from list responses (it's large). Include it only in a detail endpoint if needed later.

#### Watchlist Templates

The web app can keep using public template read access. The CLI uses admin endpoints only, so add an admin list endpoint alongside the write operations:

| Endpoint | Method | Scope | Purpose |
|---|---|---|---|
| `GET /api/admin/watchlist-templates` | GET | `admin:read` | List watchlist templates for admin/CLI usage |
| `POST /api/admin/watchlist-templates` | POST | `admin:write` | Create a new template |
| `PATCH /api/admin/watchlist-templates/{id}` | PATCH | `admin:write` | Update template name/description/sort order |
| `DELETE /api/admin/watchlist-templates/{id}` | DELETE | `admin:write` | Delete a template |
| `PUT /api/admin/watchlist-templates/{id}/packages` | PUT | `admin:write` | Replace template's package list |

#### Processed Webhook Events

Read-only access for debugging webhook issues:

| Endpoint | Method | Scope | Purpose |
|---|---|---|---|
| `GET /api/admin/webhook-events` | GET | `admin:read` | Query processed webhook events |

Query params: `?since=<datetime>&page=1&pageSize=50`

This is a diagnostic endpoint — useful for answering "did we process that Stripe webhook?" without querying the database directly.

#### Sync Operations

The existing trigger endpoint works per-package. Add a bulk trigger:

| Endpoint | Method | Scope | Purpose |
|---|---|---|---|
| `POST /api/admin/sync/trigger-all` | POST | `admin:write` | Trigger sync for all enabled packages |

### Summary: all new endpoints

| Endpoint | Method | Scope |
|---|---|---|
| `GET /api/admin/packages/{id}` | GET | `admin:read` |
| `POST /api/admin/packages` | POST | `admin:write` |
| `PATCH /api/admin/packages/{id}` | PATCH | `admin:write` |
| `DELETE /api/admin/packages/{id}` | DELETE | `admin:write` |
| `GET /api/admin/github/search` | GET | `admin:read` |
| `GET /api/admin/users` | GET | `admin:read` |
| `GET /api/admin/users/{id}` | GET | `admin:read` |
| `GET /api/admin/releases` | GET | `admin:read` |
| `GET /api/admin/summaries` | GET | `admin:read` |
| `POST /api/admin/summaries/regenerate-all` | POST | `admin:write` |
| `GET /api/admin/digest-emails` | GET | `admin:read` |
| `GET /api/admin/watchlist-templates` | GET | `admin:read` |
| `POST /api/admin/watchlist-templates` | POST | `admin:write` |
| `PATCH /api/admin/watchlist-templates/{id}` | PATCH | `admin:write` |
| `DELETE /api/admin/watchlist-templates/{id}` | DELETE | `admin:write` |
| `PUT /api/admin/watchlist-templates/{id}/packages` | PUT | `admin:write` |
| `GET /api/admin/webhook-events` | GET | `admin:read` |
| `POST /api/admin/sync/trigger-all` | POST | `admin:write` |

All new endpoints live under `/api/admin/` and use `CreateM2MAuthFilter()` + `CreateAdminFilter()`. The scope check (`admin:read` vs `admin:write`) is handled by `CreateAdminFilter()` based on the HTTP method.

### Pagination convention

The CLI only calls `/api/admin/*`, but not all admin endpoints share the same shape today. Existing admin endpoints keep their current response shapes and query params. New admin list endpoints use a consistent `page`/`pageSize` wrapper contract.

**New admin endpoints** use:

```json
{
  "items": [...],
  "total": 42,
  "page": 1,
  "pageSize": 50
}
```

Default `pageSize` is 50, max is 200. Consistent across all admin list endpoints so the CLI can use a single pagination helper.

## CLI Design

### Tool name and structure

`patchnotes` as the binary name. Subcommand-based:

```
patchnotes <command> <subcommand> [options]
```

### Commands

```
patchnotes auth login             -- Prompt for M2M client_id/secret, exchange for token, store
patchnotes auth status            -- Show current auth (client ID, scopes, token expiry)
patchnotes auth logout            -- Remove stored credentials and cached token

patchnotes packages list          -- List all packages with sync health
patchnotes packages show <id>     -- Package details
patchnotes packages add <owner/repo> [--name <name>] [--npm-name <name>]
patchnotes packages update <id> [--name <n>] [--npm-name <n>] [--tag-prefix <p>]
patchnotes packages delete <id>
patchnotes packages search <query>  -- Search GitHub repos (via /api/admin/github/search)

patchnotes sync status            -- Sync health overview (failures, disabled)
patchnotes sync trigger <id>      -- Queue package for immediate sync
patchnotes sync reset <id>        -- Clear failure tracking, re-enable
patchnotes sync disable <id>      -- Disable sync for a package
patchnotes sync trigger-all       -- Trigger sync for all enabled packages

patchnotes summaries list [--package-id <id>]  -- List summaries with operational metadata
patchnotes summaries reset <id>                -- Mark releases stale for one package
patchnotes summaries regenerate-all            -- Reset and regenerate all summaries

patchnotes releases list [--package-id <id>] [--stale] [--since <date>] [--limit 50]
patchnotes releases reset <package-id>  -- Delete releases + summaries, re-fetch

patchnotes users list [--search <q>] [--pro] [--sort createdAt|lastLoginAt]
patchnotes users show <id>        -- User details + subscription + watchlist

patchnotes digest-emails list [--user-id <id>] [--status sent|failed] [--since <date>]

patchnotes templates list         -- List watchlist templates
patchnotes templates create <name> --description <desc>
patchnotes templates update <id> [--name <n>] [--description <d>] [--sort-order <n>]
patchnotes templates delete <id>
patchnotes templates set-packages <id> --packages <pkg1,pkg2,...>

patchnotes webhook-events list [--since <date>]  -- Query processed webhook events

patchnotes email templates list
patchnotes email templates show <id>
patchnotes email send-test <template-id> --to <email>
```

### Output modes

- Default: human-readable table/text output
- `--json` flag: structured JSON output (for agents and scripting)
- `--quiet` flag: minimal output, exit code only

### Tech stack

Build as a new project in the solution: `PatchNotes.Cli`

- .NET 10 console application
- `System.CommandLine` for argument parsing
- `HttpClient` targeting the deployed API (not direct DB access)
- Shared models from `PatchNotes.Data` for type consistency

The CLI is an API client, not a second application with its own DB connection. This ensures business logic, validation, and audit logging all go through one path.

### Configuration

```
~/.config/patchnotes/credentials.json
{
  "apiUrl": "https://api.myreleasenotes.ai",
  "clientId": "m2m-client-...",
  "clientSecret": "...",
  "cachedToken": "eyJ...",
  "tokenExpiresAt": "2026-04-08T14:00:00Z"
}
```

Override with environment variables:
- `PATCHNOTES_API_URL`
- `PATCHNOTES_CLIENT_ID`
- `PATCHNOTES_CLIENT_SECRET`

Environment variables take precedence over the config file. When env vars are set, the CLI exchanges them for a fresh token on each invocation (no caching needed in CI/agent environments).

## Project structure

```
PatchNotes.Cli/
  Program.cs                    -- Entry point, command registration
  Commands/
    Auth/
      LoginCommand.cs
      StatusCommand.cs
      LogoutCommand.cs
    Packages/
      ListCommand.cs
      ShowCommand.cs
      AddCommand.cs
      ...
    Sync/
      StatusCommand.cs
      TriggerCommand.cs
      ...
  ApiClient/
    PatchNotesApiClient.cs      -- Typed HTTP client wrapping the API
    M2MAuthHandler.cs           -- DelegatingHandler: attach Bearer token, refresh if expired
  Config/
    CliConfig.cs                -- Config file + env var resolution
    CredentialStore.cs          -- Read/write credentials file + cached token
  Output/
    TableFormatter.cs           -- Human-readable table output
    JsonFormatter.cs            -- --json output
```

## Implementation order

### Phase 1: Auth infrastructure

Server-side:
- Add JWT Bearer authentication alongside existing Stytch session auth
- Configure JWKS validation against Stytch's endpoint
- Add `RouteUtils.CreateM2MAuthFilter()` for `/api/admin/*` route groups
- Update `CreateAdminFilter()` to enforce `admin:read` vs `admin:write`
- Add a CSRF bypass for Bearer-authenticated requests
- Create M2M clients in Stytch dashboard for initial testing

CLI:
- Scaffold `PatchNotes.Cli` project with `System.CommandLine`
- Implement `auth login`, `auth status`, `auth logout`
- Implement credential storage and token caching (`M2MAuthHandler`)

### Phase 2: Read-only commands

- `packages list`, `packages show`, `packages search`
- `sync status`
- `summaries list`
- `releases list`
- `users list`, `users show`

These are low-risk, read-only, and immediately useful for diagnostics.

### Phase 3: Write commands

- `packages add`, `packages update`, `packages delete`
- `sync trigger`, `sync reset`, `sync disable`, `sync trigger-all`
- `summaries reset`, `summaries regenerate-all`
- `releases reset`
- `email send-test`

### Phase 4: Agent ergonomics

- `--json` output for all commands
- `--quiet` mode
- Non-zero exit codes for errors with machine-readable error JSON
- Consider a `patchnotes exec <natural-language>` command that maps to the right subcommand (uses Ollama) — optional, nice-to-have

## Open questions

1. **Should the CLI be distributed?** As a dotnet tool (`dotnet tool install patchnotes-cli`), a standalone binary, or just built from source? Recommendation: start as a project in the solution, built from source. Package later if there's demand.

2. **Should PatchNotes.Sync be merged into the CLI?** The Sync CLI already handles `--seed`, `--init`, and sync operations. Recommendation: keep them separate for now. Sync does direct DB operations on trusted infrastructure; the CLI is an API client that works from anywhere.

3. **Stytch M2M token lifetime**: Default is 1 hour. Is that sufficient for long-running agent sessions, or should we request longer-lived tokens? Recommendation: 1 hour is fine — the CLI refreshes automatically using stored credentials.

4. **JWKS caching**: How aggressively should the API cache Stytch's JWKS? Recommendation: use the standard `JwtBearerHandler` defaults (automatic caching with background refresh). No custom caching needed.
