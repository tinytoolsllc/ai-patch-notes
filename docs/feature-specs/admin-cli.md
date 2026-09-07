# Admin CLI for PatchNotes

> A command-line tool for managing the PatchNotes application, usable by humans and AI agents.

**Status.** The admin API is complete — fifteen endpoints, built and tested. They work today with a
browser session cookie, so the operations this document describes are already available; what
remains is the CLI that makes them pleasant to use. See
[Phase 0](#phase-0-admin-api--complete) for what shipped and
[Summary: all new endpoints](#summary-all-new-endpoints) for the five proposals that were dropped
because existing routes already served them.

## Motivation

Admin operations currently require either:

- The browser (Stytch session + admin role) to hit `/api/admin/*` endpoints
- Direct database access via `PatchNotes.Sync` for sync operations

Neither works well for quick operational tasks or for AI agents that need to perform admin work in automated pipelines. A CLI tool bridges this gap.

## Goals

- Expose existing admin operations as CLI commands
- Provide a single tool for package, sync, and user management
- Support both interactive and scriptable use (`--json`, `--quiet`, meaningful exit codes) so an
  agent can drive it under the signed-in user's own credentials
- Handle authentication for a human at a terminal, without putting a long-lived secret on disk
- Avoid introducing security holes in the process

## Non-Goals

- Replacing the web UI for end users
- Building a general-purpose API client
- Adding new admin capabilities (the CLI wraps what already exists)
- **Machine-to-machine access.** An earlier version of this spec used Stytch M2M as the only auth
  path, with `admin:read`/`admin:write` scopes and a `client_secret` on disk. Every candidate caller
  turned out to be better served elsewhere: queue depth belongs in App Insights as a custom event
  from the sync function, where it also yields a time series rather than a point-in-time read; a log
  watcher reads App Insights with `az` and files issues with `gh`, touching no endpoint here; and a
  CI health check largely duplicates the deploy job's existing smoke checks. Monitoring and alerting
  are a separate concern with better tools. Revisit only if something genuinely needs to call
  `/api/admin/*` unattended.

## Authentication

This is the area that needs the most care. The current auth model is browser-centric:

- **Web users**: Stytch session cookie, validated on every request
- **Admin users**: Stytch `patch_notes_admin` role, checked by `RouteUtils.CreateAdminFilter()`
- **Sync CLI**: No auth at all (direct DB access, runs on trusted infrastructure)
- **Webhooks**: Signature verification (Stripe/Stytch secrets)

None of these work well for a CLI tool. Use **Stytch Connected Apps**, which makes this application
its own OAuth 2.0 authorization server. All auth stays in one system and there is no custom key
infrastructure to build.

Two grants, in priority order:

| Caller                          | Grant                               | Secret on disk | Status               |
| ------------------------------- | ----------------------------------- | -------------- | -------------------- |
| You, at a terminal              | Authorization Code + PKCE (browser) | none           | default              |
| You, over SSH or in a container | Device code (RFC 8628)              | none           | build only if needed |

**The loopback flow is the default and is all that is needed to reach a working CLI.** The device
flow covers the same human on a machine with no browser; Stytch does not implement it, so it has to
be built, and should be built only once SSH port forwarding has been ruled out.

Both are user flows. There is no machine-to-machine path — see [Non-Goals](#non-goals) — so **no
client secret exists anywhere in this design.**

### Interactive sign-in

Standard Authorization Code with PKCE over a loopback redirect (RFC 8252) — the same shape as
`gh auth login`. The CLI is a **public client**: Stytch issues no secret for it, the auth strategy is
`none`, and PKCE is what makes that safe.

1. The CLI generates a `code_verifier` and derives `code_challenge = S256(verifier)`, then binds an
   HTTP listener on `127.0.0.1:0` so the OS assigns a free port.
2. It opens the browser to the web app's authorize route with `client_id`, the loopback
   `redirect_uri`, `response_type=code`, `code_challenge`, `code_challenge_method=S256`, a random
   `state`, and `scope=offline_access`.
3. The user is already signed in, so they see a consent screen — Stytch's `<IdentityProvider />`
   component — and approve.
4. The browser redirects to `http://127.0.0.1:<port>/callback?code=...&state=...`.
5. The CLI's listener captures the code, verifies `state`, and serves a small "you can close this
   tab" page. Nothing is copied by hand.
6. The CLI exchanges the code for an access token and refresh token, passing `code_verifier`.
7. The API validates the bearer token locally via `IntrospectTokenLocal()` — JWKS, no Stytch call
   per request.

```
CLI                    Browser              Stytch / Web App            API
 |                        |                        |                     |
 |-- open authorize URL ->|                        |                     |
 |                        |-- sign in + consent -->|                     |
 |                        |<-- 302 to 127.0.0.1 ---|                     |
 |<-- code (loopback) ----|                        |                     |
 |------------ code + code_verifier ------------->|                     |
 |<----------- access + refresh token -------------|                     |
 |                                                                       |
 |----------------------- Bearer token ------------------------------->|
 |                                             introspect locally        |
 |<-------------------------------------------------------------- 200 --|
```

Availability is already in place: `@stytch/react@20.0.5`, the version in the lockfile, exports
`IdentityProvider` from the consumer SDK. No upgrade is required.

**Redirect URL registration.** Register `http://127.0.0.1/callback` on the public client with the
**port omitted**. Stytch reads a missing port as "any port", which is what allows the CLI to bind a
free port at runtime rather than colliding on a hard-coded one.

### Authorization for the interactive flow

**No scopes at all.** The access token identifies the signed-in user, so the existing
`patch_notes_admin` role check in `RouteUtils.CreateAdminFilter()` applies unchanged.

This is a real simplification over a scope-based model:

- Nothing to keep in sync between Stytch scope definitions and route-level checks
- Revocation is revoking a session, not rotating a shared secret
- The audit trail names a person rather than a robot identity

### CLI-side token management

- `auth login` runs the PKCE browser flow above. There is no prompt for a secret because there is no
  secret.
- Tokens go in the OS keychain where one is available, falling back to
  `~/.config/patchnotes/credentials.json` at mode `600`.
- The refresh token (from `scope=offline_access`) is used to obtain a new access token silently, so a
  normal session never re-opens the browser.
- `auth status` reports the signed-in user and token expiry; `auth logout` clears stored tokens.

Two implementation details that are easy to get wrong and matter:

- Bind the listener to `127.0.0.1`, not `0.0.0.0`. The latter makes the callback reachable from
  anywhere on the network.
- Use `S256`, never `plain`.

### Device flow, for terminals without a browser (RFC 8628)

The loopback flow above needs a browser on the _same machine_ as the CLI. Over SSH, inside a
container, or on a headless box it cannot work — there is nothing to open, and `127.0.0.1` on the
remote host is not the laptop the user is sitting at.

**Try the cheap fix first.** SSH port forwarding makes the loopback flow work unchanged:

```
ssh -L 8976:127.0.0.1:8976 remote-host    # then run the CLI with a fixed port
```

VS Code Remote and the JetBrains gateway already forward loopback ports automatically, so in those
environments nothing is needed. Build the device flow only if a real case survives that.

#### Stytch does not implement this grant

Checked against the [Connected App token endpoint](https://stytch.com/docs/api/connected-app-token):
it accepts `authorization_code` and `refresh_token` only. There is no
`urn:ietf:params:oauth:grant-type:device_code`, and Connected Apps is the authorization server here,
so this cannot be switched on — it has to be built.

The design below keeps **Stytch as the only token issuer**. The API gains device-flow endpoints, but
it never mints a token, never holds a signing key, and never sees an access token. What it brokers is
an authorization code, which is useless to anyone who does not hold the PKCE verifier — and only the
CLI does.

#### Flow

```
CLI (remote)              API                     Browser (laptop)         Stytch
 |                         |                            |                    |
 |-- POST device/code ---->|                            |                    |
 |   + code_challenge      |                            |                    |
 |<-- user_code,          -|                            |                    |
 |    device_code,         |                            |                    |
 |    verification_uri,    |                            |                    |
 |    interval             |                            |                    |
 |                         |                            |                    |
 |   [displays user_code, starts polling]               |                    |
 |                         |<-- opens verification_uri -|                    |
 |                         |    signs in + enters code  |                    |
 |                         |--- authorize (challenge) ------------------->   |
 |                         |<-- redirect with code ----------------------    |
 |                         |   [stores code against device_code]         |
 |-- POST device/token --->|                            |                    |
 |   + device_code         |                            |                    |
 |<-- authorization code --|                            |                    |
 |                         |                            |                    |
 |------------- code + code_verifier ------------------------------------>   |
 |<------------ access + refresh token -----------------------------------   |
```

The CLI performs the token exchange itself, with the verifier it generated in step one. The API is a
relay for the authorization code and nothing more.

#### Endpoints

| Endpoint                         | Method | Auth    | Purpose                                       |
| -------------------------------- | ------ | ------- | --------------------------------------------- |
| `POST /api/cli/device/code`      | POST   | none    | Start a device authorization                  |
| `POST /api/cli/device/token`     | POST   | none    | Poll for the authorization code               |
| `GET /api/cli/device/callback`   | GET    | none    | Stytch redirect target; binds code to request |
| `POST /api/admin/device/approve` | POST   | session | Called by the web page once the user approves |

`POST /api/cli/device/code` takes `client_id` and `code_challenge`, and returns `device_code`,
`user_code`, `verification_uri`, `expires_in` and `interval`, per RFC 8628 §3.2.

`POST /api/cli/device/token` takes `device_code` and returns RFC 8628 §3.5 errors while pending:
`authorization_pending`, `slow_down`, `expired_token`, `access_denied`. On success it returns the
authorization code once, then invalidates it.

#### Web page

A route in `patchnotes-web` at the `verification_uri` — a short path worth typing, `/device`. The
user is signed in already or signs in normally; they enter the `user_code`, see what is being
authorized, and approve. On approval the page triggers the Connected Apps authorization with the
CLI's `code_challenge` and the API callback as `redirect_uri`.

Register that callback as a second redirect URL on the public client, alongside the loopback one.

#### Storage

One table, or a distributed cache entry, keyed by `device_code`:

| Field            | Note                                                     |
| ---------------- | -------------------------------------------------------- |
| `device_code`    | `IdGenerator.NewId()` — the polling credential           |
| `user_code`      | `IdGenerator.NewId()` — what the user types              |
| `code_challenge` | passed straight through to Stytch; never used by the API |
| `status`         | `pending` / `approved` / `denied` / `expired`            |
| `auth_code`      | populated at callback, cleared on first successful poll  |
| `expires_at`     | 10 minutes                                               |
| `approved_by`    | user ID, for the audit trail                             |

Both come from `IdGenerator.NewId()` in `PatchNotes.Data` — the same generator behind every entity
ID. It is already CSPRNG-backed (`RandomNumberGenerator.Fill`) over a 64-character alphabet at length
21, which is ~126 bits: far more than RFC 8628 asks of a user code, and no new randomness code to
review.

The trade-off is that 21 mixed-case characters including `_` and `-` are tedious to read off one
screen and type into another. If that turns out to matter, give `IdGenerator` a size-and-alphabet
overload and call it with shorter arguments — one generator with parameters, rather than a second
bespoke alphabet living in the device-flow code.

#### Security

These endpoints are unauthenticated by necessity, which makes the details load-bearing:

- **Rate limit `device/token` per `device_code`.** Honour the `interval`, and return `slow_down` and
  increase it when a client polls faster.
- **Rate limit `user_code` submission per session and per IP.** Brute force is the obvious attack on
  this endpoint. At full `IdGenerator` length guessing is not realistic, but the rate limit is what
  keeps that true if the code is ever shortened for usability.
- **Single use.** The authorization code is returned exactly once and cleared. A second poll gets
  `expired_token`.
- **Short TTL.** Ten minutes, and expiry is enforced on read as well as by a sweep.
- **Show what is being approved.** The approval page names the client and warns if the user did not
  initiate it — device flow's real-world failure mode is phishing a user into approving someone
  else's terminal.
- **No tokens at rest.** The API stores an authorization code bound to a challenge it cannot solve.
  If the table leaks, the codes in it are unusable without the CLI's verifier.

#### Cost

One table (both migration contexts), four endpoints, one web route, and the polling loop in the CLI.
Meaningfully more than the loopback flow, which is why it is not the default and why the SSH
forwarding workaround is worth ruling out first.

### Trade-offs

**Accepted:**

- The browser flow needs a browser on the same machine. Over SSH or in a container, forward the
  loopback port, or build the device flow above.
- If Stytch is down, no new tokens can be issued; cached access tokens keep working until expiry.
- Client management happens in the Stytch dashboard, not in the CLI.

**Avoided:**

- No custom `ApiKey` table, no migration, no key hashing, no bootstrap problem
- No custom auth middleware — standard bearer validation only
- No credential lifecycle to build; Stytch handles rotation, revocation and expiry
- For the interactive path specifically: no secret to store, leak, or rotate at all

### Cost

Nothing. The authorization code and refresh token flows are part of Connected Apps, and the CLI
holds a long-lived refresh token, so a working session costs no per-request Stytch calls and no
metered token issuance.

### Security considerations

- **Credential storage**: access and refresh tokens only, in the OS keychain where available,
  otherwise a `600` file. No client secret exists anywhere in this design.
- **Revocation**: revoke the Stytch session or the app's consent. Issued access tokens remain valid
  until expiry (max 1 hour).
- **CSRF**: the `state` parameter is generated per login and verified on the callback.
- **Audit logging**: log the authenticated user ID on each admin request to Application Insights.
- **Never log tokens.** Log only the user ID, for traceability.

### Server-side changes

The API currently resolves authenticated browser requests in `RouteUtils.CreateAuthFilter()` by validating the `stytch_session` cookie via a Stytch API call on every request. Add Bearer token validation as a second auth path for admin route groups. The token is verified locally against Stytch's JWKS, so this path costs no Stytch API call per request.

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

Also add a CSRF bypass for Bearer-authenticated requests in `CsrfMiddleware`. The CLI doesn't send `Origin` or `Sec-Fetch-Site` headers, and a bearer token is not attached by a browser, so CSRF does not apply:

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

#### 2. Add CreateBearerAuthFilter() for admin routes only

Do **not** modify the existing `CreateAuthFilter()`. Instead, add a separate filter that also accepts Bearer tokens. It applies only to `/api/admin/*` routes, so user, watchlist and subscription endpoints remain session-cookie-only.

The filter records how the caller authenticated in `httpContext.Items["AuthMethod"]` — `"session"` or `"bearer"` — which is useful for audit logging, though nothing downstream branches on it.

```csharp
/// <summary>
/// Auth filter that accepts a Stytch session cookie (existing) or a
/// Connected Apps Bearer token. Admin routes only.
/// </summary>
public static Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate>
    CreateBearerAuthFilter()
{
    return (context, next) => async invocationContext =>
    {
        var httpContext = invocationContext.HttpContext;

        // Path 1: Bearer token from the CLI
        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
        {
            var result = await httpContext.AuthenticateAsync(
                JwtBearerDefaults.AuthenticationScheme);
            if (!result.Succeeded)
                return Results.Unauthorized();

            // The token identifies a user, so populate the same items the
            // session path does. CreateAdminFilter() then works unchanged.
            httpContext.Items["StytchUserId"] =
                result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            httpContext.Items["AuthMethod"] = "bearer";

            return await next(invocationContext);
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

Whatever resolves the `patch_notes_admin` role from a user ID today needs to work for a Bearer
caller too. If the role currently comes off the `StytchSession` object rather than a lookup by user
ID, that lookup is the one piece of real work in this filter.

Admin route groups use `CreateBearerAuthFilter()` instead of `CreateAuthFilter()`:

```csharp
// In PackageRoutes.cs (and other admin route files):
var adminPackages = app.MapGroup("/api/admin/packages")
    .AddEndpointFilterFactory(RouteUtils.CreateBearerAuthFilter())
    .AddEndpointFilterFactory(RouteUtils.CreateAdminFilter());
```

Non-admin authenticated routes (`/api/users/me`, `/api/watchlist`, `/api/subscription/*`) continue using the existing `CreateAuthFilter()` unchanged — they only accept session cookies.

#### 3. CreateAdminFilter() needs no changes

Both paths above put a Stytch user ID in `httpContext.Items["StytchUserId"]`, so the existing
`patch_notes_admin` role check applies to a CLI caller exactly as it does to a browser. There is no
scope model, no second authorization path, and nothing to keep in sync.

## Admin API Surface

The CLI is an API client, so every CLI command needs a backing endpoint. This section maps all 10 database entities to admin endpoints, noting what already exists and what's new.

Design rule: the PatchNotes CLI only talks to `/api/admin/*` endpoints on the PatchNotes API. If an equivalent non-admin endpoint already exists, add a separate admin-path endpoint for CLI usage rather than calling the non-admin route directly. This keeps admin-tool usage patterns separate from browser/public/session-based flows.

### Existing admin endpoints (no changes needed)

These are already implemented and just need to accept Bearer tokens alongside session cookies:

| Endpoint                                   | Method | CLI command                    |
| ------------------------------------------ | ------ | ------------------------------ |
| `/api/admin/packages/health`               | GET    | `packages list`, `sync status` |
| `/api/admin/packages/{id}/reset-sync`      | POST   | `sync reset`                   |
| `/api/admin/packages/{id}/disable-sync`    | POST   | `sync disable`                 |
| `/api/admin/packages/{id}/trigger-sync`    | POST   | `sync trigger`                 |
| `/api/admin/packages/{id}/reset-summaries` | POST   | `summaries reset`              |
| `/api/admin/packages/{id}/reset-releases`  | POST   | `releases reset`               |
| `/api/admin/email-templates`               | GET    | `email templates list`         |
| `/api/admin/email-templates/{id}`          | GET    | `email templates show`         |
| `/api/admin/email-templates/{id}`          | PUT    | `email templates update`       |
| `/api/admin/email-templates/{id}/test`     | POST   | `email send-test`              |

### New admin endpoints needed

#### Packages and GitHub search

Only one package endpoint was missing, and it belongs on `/api/packages` beside the existing
`PATCH` and `DELETE`, which already run behind the same auth and admin filters an admin route would
apply. `/api/admin/packages` is for operational actions -- health, reset-sync, disable-sync,
trigger-sync -- not CRUD.

| Endpoint             | Method | Status | Purpose                            |
| -------------------- | ------ | ------ | ---------------------------------- |
| `POST /api/packages` | POST   | Built  | Start tracking a GitHub repository |

Until this existed, a package could only be created as a side effect of a user adding it to their
watchlist, so tracking something nobody watches yet was impossible.

Request body:

```json
{
  "owner": "facebook",
  "repo": "react",
  "name": "React",
  "npmName": "react",
  "tagPrefix": "v"
}
```

Only `owner` and `repo` are required. `name` defaults to the repository name; `npmName` is optional,
since some tracked repos are not npm packages; `tagPrefix` is for repositories that publish several
products from one tag namespace.

Returns `201` with the created package, `409` if that owner/repo is already tracked, and `400` for
an invalid segment. `LastFetchedAt` is left null, which is how the sync job recognises a package it
has never seen — creation does not trigger a sync, the next scheduled run picks it up.

The duplicate check and defaults match the watchlist creation path exactly, so a package created
either way is indistinguishable.

**Not built, and why.** `GET`, `PATCH` and `DELETE` under `/api/admin/packages/{id}` were dropped:
`PATCH /api/packages/{id}` and `DELETE /api/packages/{id}` are already gated with `requireAuth` +
`requireAdmin`, and `GET /api/packages/{id}` is public. `GET /api/admin/github/search` was dropped
for the same reason — `GET /api/github/search?q=...` already exists behind authentication.

#### Users

No admin user endpoints exist today. All current user endpoints are `/api/users/me` (self-service).

| Endpoint                    | Method | Purpose                                   |
| --------------------------- | ------ | ----------------------------------------- |
| `GET /api/admin/users`      | GET    | List users with pagination                |
| `GET /api/admin/users/{id}` | GET    | User detail with subscription + watchlist |

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
  "limit": 50,
  "offset": 0
}
```

Query params: `?limit=50&offset=0&search=<email or name>&pro=true|false&sort=createdAt|lastLoginAt`

`GET /api/admin/users/{id}` includes the full user record plus their watchlist packages and recent digest email history.

#### Releases

**Not built.** `GET /api/releases` already serves paginated release queries, so an admin mirror
would be a second path to the same data.

The one filter it lacks is staleness — "which releases are still waiting for a summary?" — and that
question is answered better by `GET /api/admin/summaries/queue`, which reports it per package along
with why each entry is queued and whether it can still affect a summary at all. A `stale=true`
filter on a flat release list would return rows without any of that context.

#### Release Summaries

The public `/api/summaries` endpoint returns summaries grouped by package. Add an admin view for operational queries:

| Endpoint                                   | Method | Purpose                                               |
| ------------------------------------------ | ------ | ----------------------------------------------------- |
| `GET /api/admin/summaries`                 | GET    | Query summaries with operational metadata             |
| `POST /api/admin/summaries/regenerate-all` | POST   | Mark all releases stale, triggering full regeneration |

`GET /api/admin/summaries` query params: `?packageId=<id>&limit=50&offset=0`

Response includes `generatedAt`, `updatedAt`, and the count of stale releases per group — information the public endpoint doesn't expose.

`POST /api/admin/summaries/regenerate-all` marks every release as `SummaryStale = true` and deletes all `ReleaseSummary` rows. The next sync cycle picks up the regeneration work.

##### Summarization queue

The "queue" is not a table — it is whatever `SummaryGenerationService.GenerateAllSummariesAsync`
selects on each run:

- releases with `SummaryStale = true`, and
- `ReleaseSummary` rows whose `Summary` is null or empty

Both are unioned into a distinct list of package IDs, and every one of those packages gets a
group-summary regeneration attempt. Nothing bounds how long an entry stays in that set: a group
that keeps failing is retried on every sync run, indefinitely. The only escape hatch today is the
`HttpRequestException` 400 handler, which clears `SummaryStale` explicitly to "break the infinite
retry loop".

That became a real problem on 2026-09-03, when the AI provider's free tier hit its quota and began
returning 429. Because 429 falls into the generic `catch (Exception)` branch, nothing was cleared,
the queue only grew, and per-3h call volume climbed 8 → 16 → 39 → 73 → 101 while every single call
failed. There was no way to see the queue depth without querying the database directly, and no way
to drain it.

| Endpoint                            | Method | Purpose                          |
| ----------------------------------- | ------ | -------------------------------- |
| `GET /api/admin/summaries/queue`    | GET    | Inspect what is pending, and why |
| `DELETE /api/admin/summaries/queue` | DELETE | Drain entries from the queue     |

`GET /api/admin/summaries/queue` returns, per queued package:

- `packageId`, `packageName`
- `reason` — `stale-release` or `empty-summary` (a package can be queued for both)
- `staleReleaseCount`, and the `PublishedAt` of the oldest and newest stale release
- `queuedSince` — the oldest `FetchedAt` among its stale releases, which is how long this package
  has been failing to summarize
- `outOfWindow` — true when every stale release is older than `SummaryConstants.SummaryWindow`
  (7 days) behind that group's newest release

`outOfWindow` is the useful one. `GenerateGroupSummaryAsync` computes
`cutoff = newest.PublishedAt - SummaryWindow` and only sends releases at or after the cutoff to the
model, so a release further back than that can never influence the summary text. It still carries
`SummaryStale = true` and still keeps its package queued, but it can never be the reason a summary
changes. Those entries are pure cost.

Query params: `?outOfWindowOnly=true&limit=50&offset=0`. `outOfWindowOnly` narrows the list to
packages whose stale releases are _all_ out of window — the ones a drain would empty entirely.
The `DELETE` below works per release rather than per package, so it also trims out-of-window
releases from packages that still have in-window work; both measure against `SummaryWindow`.

Summary counters at the top of the response: total queued packages, total stale releases, count
queued only for `out-of-window` releases, and age of the oldest entry.

`DELETE /api/admin/summaries/queue` takes a filter so draining is deliberate rather than a blunt
reset:

- `?scope=out-of-window` — clear `SummaryStale` only on releases that can no longer affect any
  summary. Safe by construction: nothing that would have changed the output is dropped.
- `?scope=package&packageId=<id>` — drain one package
- `?scope=all` — clear every `SummaryStale` flag and delete empty `ReleaseSummary` rows

Note this is the inverse of `regenerate-all`, and the two should not be confused:
`regenerate-all` _fills_ the queue, `DELETE .../queue` _empties_ it. Draining does not delete
existing summaries; it only stops pending work from being retried. Anything drained will re-enter
the queue naturally the next time that package publishes a release.

#### Sent Digest Emails

No endpoints exist for this table. Add read-only admin access:

| Endpoint                       | Method | Purpose                   |
| ------------------------------ | ------ | ------------------------- |
| `GET /api/admin/digest-emails` | GET    | Query sent digest history |

Query params: `?userId=<id>&status=sent|failed|pending&since=<datetime>&limit=50&offset=0`

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
  "limit": 50,
  "offset": 0
}
```

The `htmlBody` field is excluded from list responses (it's large). Include it only in a detail endpoint if needed later.

#### Watchlist Templates

Management lives on the same path as the existing public read, not a parallel admin tree. The `GET`
stays public because onboarding needs it; each mutation is admin-gated individually.

| Endpoint                                     | Method | Status  | Purpose                         |
| -------------------------------------------- | ------ | ------- | ------------------------------- |
| `GET /api/watchlist/templates`               | GET    | Existed | Public list, now with sortOrder |
| `POST /api/watchlist/templates`              | POST   | Built   | Create a template               |
| `PATCH /api/watchlist/templates/{id}`        | PATCH  | Built   | Partial update                  |
| `DELETE /api/watchlist/templates/{id}`       | DELETE | Built   | Delete a template               |
| `PUT /api/watchlist/templates/{id}/packages` | PUT    | Built   | Replace the package list        |

There is no separate admin list view. The public shape already carries the packages and their ids;
the only thing it lacked was `SortOrder`, which anything editing templates needs because `PATCH`
takes an absolute value rather than a move. That field is now returned, which is additive and leaves
existing clients unaffected.

`PATCH` only touches fields the request actually contains, so updating the sort order cannot blank
the description. `PUT /{id}/packages` replaces membership wholesale and validates every id before
writing anything, so one bad id cannot leave a template half-updated. `DELETE` removes the template
and its membership rows but never the packages themselves -- a template is a curated list, not an
owner, and users who already applied it keep their watchlist.

#### Processed Webhook Events

Read-only access for debugging webhook issues:

| Endpoint                        | Method | Purpose                        |
| ------------------------------- | ------ | ------------------------------ |
| `GET /api/admin/webhook-events` | GET    | Query processed webhook events |

Query params: `?since=<datetime>&limit=50&offset=0`

This is a diagnostic endpoint — useful for answering "did we process that Stripe webhook?" without querying the database directly.

#### Sync Operations

**Not built.** A bulk trigger was specified and implemented, then removed: it could not do what its
name claimed, and what it actually did was harmful.

`LastFetchedAt` is read in exactly two places — `SyncService.cs` uses it as `since`, the early-stop
bound that makes an incremental fetch stop after one page, and `SyncNewPackagesFunction` selects
rows where it is null. Nothing selects packages _for_ the hourly sync by it; `SyncPipeline` and
`SyncAllAsync` both take every package with `IsSyncDisabled == false`. So every enabled package was
already being synced every hour, and "queue them for the next run" described work that was already
scheduled.

Nulling the column did have an effect, just not that one. It removed the early-stop bound
catalogue-wide, turning the next run into a full re-pagination of every repository's entire release
history for no new data, and handed that to one serial function with a ten-minute timeout. It also
blanked the timestamp the admin health page reports, and the public package page treats a missing
`LastFetchedAt` with no version groups as "syncing" — so every affected package showed a spinner and
polled every 30 seconds until a sync completed. Nothing restored the old values.

The per-package `POST /api/admin/packages/{id}/trigger-sync` remains, which is the case that
actually comes up. A genuine catalogue-wide re-fetch is a maintenance operation, not an API call.

### Summary: all new endpoints

Thirteen endpoints were added and one existing endpoint extended. The rest of the original
proposal was either already served by an existing route or turned out not to be worth building —
see below. Five of what shipped moved off `/api/admin/`, for the reason in the next section.

| Endpoint                                     | Method | Status                           |
| -------------------------------------------- | ------ | -------------------------------- |
| `POST /api/packages`                         | POST   | Built                            |
| `GET /api/admin/summaries`                   | GET    | Built                            |
| `GET /api/admin/summaries/queue`             | GET    | Built                            |
| `DELETE /api/admin/summaries/queue`          | DELETE | Built                            |
| `POST /api/admin/summaries/regenerate-all`   | POST   | Built                            |
| `GET /api/admin/users`                       | GET    | Built                            |
| `GET /api/admin/users/{id}`                  | GET    | Built                            |
| `GET /api/admin/digest-emails`               | GET    | Built                            |
| `GET /api/admin/webhook-events`              | GET    | Built                            |
| `POST /api/watchlist/templates`              | POST   | Built                            |
| `PATCH /api/watchlist/templates/{id}`        | PATCH  | Built                            |
| `DELETE /api/watchlist/templates/{id}`       | DELETE | Built                            |
| `PUT /api/watchlist/templates/{id}/packages` | PUT    | Built                            |
| `GET /api/watchlist/templates`               | GET    | Existed, now returns `sortOrder` |

Every one uses `CreateAuthFilter()` + `CreateAdminFilter()` — the same `patch_notes_admin` role
check that guards the admin UI today — except the template `GET`, which stays public because
onboarding needs it.

#### Where an endpoint lives

`/api/admin/` is not "everything the CLI touches". The repository already draws a sharper line, and
these endpoints follow it:

- **`/api/<resource>`** — the resource itself. Reads may be public; mutations are admin-gated per
  route. `POST /api/packages` sits beside the existing `PATCH` and `DELETE`; the template mutations
  sit beside the existing `GET`.
- **`/api/admin/<resource>`** — operational actions on a resource, and resources with no public face
  at all. `packages/{id}/reset-sync`, `summaries/queue`, `users`,
  `digest-emails`, `webhook-events`.

The rule that matters: **one resource, one path.** Creating a package under `/api/admin/packages`
while updating and deleting it on `/api/packages` would have split a single resource across two
paths, with create separated from update for no reason. The same applies to templates — management
belongs with the read, not in a parallel tree.

#### Dropped: five endpoints that already exist

| Proposed                          | Already served by                                     |
| --------------------------------- | ----------------------------------------------------- |
| `GET /api/admin/packages/{id}`    | `GET /api/packages/{id}` — public                     |
| `PATCH /api/admin/packages/{id}`  | `PATCH /api/packages/{id}` — **already admin-gated**  |
| `DELETE /api/admin/packages/{id}` | `DELETE /api/packages/{id}` — **already admin-gated** |
| `GET /api/admin/github/search`    | `GET /api/github/search` — authenticated              |
| `GET /api/admin/releases`         | `GET /api/releases` — public, paginated               |

The two package mutations are the telling ones: they already run behind exactly the filters an admin
route would apply, so mirroring them would have created a second implementation of the same
operation.

These mirrors existed because of a constraint that no longer applies. Under the original M2M design
the CLI held a machine identity with no user, M2M tokens were not accepted on non-admin routes, and
so everything the CLI touched had to be duplicated under `/api/admin/`. With the CLI authenticating
as the signed-in user it can call any endpoint that user is authorized for. Verified directly: a
browser session cookie returns 200 from `/api/users/me`, `/api/watchlist` and
`/api/admin/packages/health` alike.

#### CSRF applies to every mutating call

`CsrfMiddleware` exempts `GET`, `HEAD` and `OPTIONS`, and requires an `Origin` header matching
`AllowedOrigins` on everything else. It runs **before** authentication, so a `POST` without `Origin`
is refused as CSRF and never reaches the auth filter — an unauthenticated call returns 403, not 401.

Anything driving this API from outside a browser must send `Origin` on every mutating request:

```bash
curl -X DELETE -b "stytch_session=$SESSION" \
     -H "Origin: https://app.myreleasenotes.ai" \
     "https://api.myreleasenotes.ai/api/admin/summaries/queue?scope=out-of-window"
```

### Pagination convention

The CLI calls both `/api/admin/*` and the admin-gated routes on `/api/<resource>`, and not all of them share the same shape today. Existing endpoints keep their current response shapes and query params. New list endpoints reuse the repository's existing `PaginatedResponse<T>` rather than introducing a second pagination contract alongside it.

**New list endpoints** return:

```json
{
  "items": [...],
  "total": 42,
  "limit": 50,
  "offset": 0
}
```

Default `limit` is 50, max is 200, clamped server-side. This is the shape `GET /api/packages` and `GET /api/releases` already return, so the CLI needs one pagination helper for the whole API rather than one per tree.

## CLI Design

### Tool name and structure

`patchnotes` as the binary name. Subcommand-based:

```
patchnotes <command> <subcommand> [options]
```

### Commands

```
patchnotes auth login             -- Open browser, approve access, store tokens (no secret)
patchnotes auth status            -- Show current auth (signed-in user, token expiry)
patchnotes auth logout            -- Remove stored tokens

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

patchnotes summaries list [--package-id <id>]  -- List summaries with operational metadata
patchnotes summaries reset <id>                -- Mark releases stale for one package
patchnotes summaries regenerate-all            -- Reset and regenerate all summaries
patchnotes summaries queue                     -- Show the summarization backlog and why each entry is queued
patchnotes summaries queue --out-of-window     -- Only entries that can no longer affect any summary
patchnotes summaries drain --out-of-window     -- Clear entries that can never change output (safe)
patchnotes summaries drain --package-id <id>   -- Drain one package
patchnotes summaries drain --all               -- Clear the whole backlog (does not delete summaries)

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

Tokens live in the OS keychain where one is available. The fallback file holds no secret beyond the
tokens themselves — there is no client secret to store:

```
~/.config/patchnotes/credentials.json     # mode 600
{
  "apiUrl": "https://api.myreleasenotes.ai",
  "accessToken": "eyJ...",
  "refreshToken": "...",
  "expiresAt": "2026-04-08T14:00:00Z"
}
```

Override the API URL with `PATCHNOTES_API_URL`, which is useful against a local API. The
`client_id` is not configurable — it is a build constant, since a public client ID is not a secret.

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
    BearerAuthHandler.cs        -- DelegatingHandler: attach access token, refresh if expired
    PkceLoginFlow.cs            -- Loopback listener, PKCE challenge, state verification
  Config/
    CliConfig.cs                -- Config file + env var resolution
    TokenStore.cs               -- OS keychain, falling back to a 600 credentials file
  Output/
    TableFormatter.cs           -- Human-readable table output
    JsonFormatter.cs            -- --json output
```

## Implementation order

### Phase 0: Admin API — **complete**

All fifteen endpoints are built and tested, across five stacked pull requests. They are
usable today with a browser session cookie; none of the auth work below is needed to call them.

| Layer                    | Endpoints                                                                    |
| ------------------------ | ---------------------------------------------------------------------------- |
| Summaries, read          | `GET /summaries`, `GET /summaries/queue`                                     |
| Summaries, write         | `DELETE /summaries/queue`, `POST /summaries/regenerate-all`                  |
| Packages                 | `POST /packages`                                                             |
| Users, digests, webhooks | `GET /users`, `GET /users/{id}`, `GET /digest-emails`, `GET /webhook-events` |
| Watchlist templates      | `GET`, `POST`, `PATCH`, `DELETE`, `PUT /{id}/packages`                       |

Two behaviours are deliberate and worth carrying into the CLI design:

- **`regenerate-all` requires `confirm=true`.** Unconfirmed it reports the blast radius and changes
  nothing. It deletes every summary and re-queues everything, so if the AI provider is refusing —
  an exhausted quota, say — the summaries do not come back. The CLI should prompt before passing
  the flag.
- **`DELETE /summaries/queue` defaults to `scope=out-of-window`**, the only scope that cannot
  discard useful work. `scope=all` exists but should never be the default in a client.

### Phase 1: Auth infrastructure

Everything from here down is CLI ergonomics, not access. The API is already reachable.

Register a **public** client in Stytch first (redirect `http://127.0.0.1/callback`, port omitted).
No secret is issued and none is needed anywhere in this design.

Web:

- Mount Stytch's `<IdentityProvider />` on an authorize route in `patchnotes-web`. The CLI cannot
  authenticate against the API alone; the browser half of the flow has to live somewhere.

Server-side:

- Add Bearer token validation alongside existing Stytch session auth
- Configure JWKS validation against Stytch's endpoint
- Add `RouteUtils.CreateBearerAuthFilter()` for `/api/admin/*` route groups
- Leave `CreateAdminFilter()`'s role check as-is — an interactive token carries a user, so it needs
  no scope handling
- Add a CSRF bypass for Bearer-authenticated requests

CLI:

- Scaffold `PatchNotes.Cli` project with `System.CommandLine`, added to `PatchNotes.slnx`
- Implement the PKCE loopback flow: `code_verifier`/`code_challenge`, a `127.0.0.1:0` listener,
  `state` verification
- Implement `auth login`, `auth status`, `auth logout`
- Implement token storage (OS keychain, falling back to a `600` file) and silent refresh

### Phase 2: Read-only commands

- `packages list`, `packages show`, `packages search`
- `sync status`
- `summaries list`
- `summaries queue`
- `releases list`
- `users list`, `users show`

These are low-risk, read-only, and immediately useful for diagnostics.

`summaries queue` is worth pulling to the front of this phase. During the 2026-09-03 AI quota
outage the queue grew unbounded for roughly fifteen hours with no way to observe it short of
querying production directly, and the first useful question — "how much of this backlog is even
capable of producing a summary?" — had no answer. It is read-only and depends on nothing else here.

### Phase 3: Write commands

- `packages add`, `packages update`, `packages delete`
- `sync trigger`, `sync reset`, `sync disable`
- `summaries reset`, `summaries regenerate-all`
- `summaries drain` (start with `--out-of-window`, which cannot discard useful work)
- `releases reset`
- `email send-test`

### Phase 4: Agent ergonomics

- `--json` output for all commands
- `--quiet` mode
- Non-zero exit codes for errors with machine-readable error JSON
- Consider a `patchnotes exec <natural-language>` command that maps to the right subcommand (uses Ollama) — optional, nice-to-have

### Phase 5: Device flow (only if needed)

Skip unless someone is actually blocked running the CLI over SSH or in a container, and SSH port
forwarding has not solved it.

- Device authorization table plus migrations for both contexts
- `POST /api/cli/device/code`, `POST /api/cli/device/token`, `GET /api/cli/device/callback`
- `POST /api/admin/device/approve`, called by the web page
- `/device` route in `patchnotes-web` for entering the user code and approving
- Second redirect URL registered on the public client, for the API callback
- Polling loop in the CLI honouring `interval` and `slow_down`
- Rate limits on code submission and polling, which are the whole security story here

## Open questions

0. **Do we need the device flow?** The loopback flow needs a browser on the same machine, which fails over SSH and in containers. Stytch does not implement RFC 8628, so it would be built here — the design is in [Device flow](#device-flow-for-terminals-without-a-browser-rfc-8628) and costs a table, four endpoints and a web route. Recommendation: try `ssh -L` port forwarding first, which makes the loopback flow work with no new code, and build the device flow only if a case survives that.

1. **Should the CLI be distributed?** As a dotnet tool (`dotnet tool install patchnotes-cli`), a standalone binary, or just built from source? Recommendation: start as a project in the solution, built from source. Package later if there's demand.

2. **Should PatchNotes.Sync be merged into the CLI?** The Sync CLI already handles `--seed`, `--init`, and sync operations. Recommendation: keep them separate for now. Sync does direct DB operations on trusted infrastructure; the CLI is an API client that works from anywhere.

3. **Token lifetime**: Access tokens default to 1 hour. Recommendation: leave it. The CLI stores a refresh token (`scope=offline_access`) and renews silently, so the browser only re-opens when the refresh token itself expires.

4. **JWKS caching**: How aggressively should the API cache Stytch's JWKS? Recommendation: use the standard `JwtBearerHandler` defaults (automatic caching with background refresh). No custom caching needed.
