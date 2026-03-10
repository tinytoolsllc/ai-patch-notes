# Known Bugs

## AI summary generation fails permanently for packages with large release notes

**Status:** Open
**Affected package:** tauri-apps/tauri (ID: `VOGsDSjkGvz23dlDCPFwA`)
**First observed:** At least 2026-03-09 (likely earlier)

### Symptoms

- Every hourly sync run reports exactly 2 summary errors for this package
- The AI API (Groq, gemma3:27b) returns **400 Bad Request**
- The errors have been repeating on every single sync run with no resolution

### Root cause

Tauri has very long release notes. `SummaryGenerationService.GenerateGroupSummaryAsync` collects all releases within a 7-day window for each version group and sends the full release bodies to the AI API via `AiClient.SummarizeReleaseNotesAsync`. For this package, the combined payload exceeds the model's token/context limit, causing a 400.

The package has 2 version groups with stale releases (2 errors per run). When the AI call fails, the catch block in `SummaryGenerationService.cs:127-134` logs the error and records it in `SummaryGenerationError`, but the releases remain marked `SummaryStale = true`. This means the next sync run picks them up again, creating an **infinite retry loop** that will never succeed.

### Relevant code

- `PatchNotes.Sync.Core/SummaryGenerationService.cs` — lines 82-134 (stale check + catch block)
- `PatchNotes.Sync.Core/AI/AiClient.cs` — line 70 (`response.EnsureSuccessStatusCode()` throws on 400)
- `PatchNotes.Data/SummaryConstants.cs` — 7-day summary window

### Suggested fix

1. **Truncate input:** Cap the release body length (e.g., first 4000 chars per release) in `GenerateGroupSummaryAsync` before sending to the AI API
2. **Break the retry loop:** On non-retryable errors (400), mark releases as `SummaryStale = false` and store a placeholder summary so the package isn't retried every run
3. Ideally both — truncate to prevent the 400, and add a circuit breaker for any persistent failures

---

## Digest email fails to render — esbuild binary not executable on Azure Functions

**Status:** Fix in progress
**First observed:** 2026-03-06T09:00:00Z

### Symptoms

- The `sendDigest` function correctly matches users (logged `"Sending digests to 1 users"`)
- Template rendering crashes before any email is sent
- Error: `spawn /home/site/wwwroot/node_modules/@esbuild/linux-x64/bin/esbuild EACCES`
- The function then throws, logging `"Digest summary: 0 sent, 1 failed, 0 skipped out of 1 users"`
- Since the digest only fires once per week (matching `DigestDay`+`DigestHour`), the failure isn't retried until the next week
- Also caused 403s on the admin email template preview endpoint (the function had IP access restrictions limited to `AppService.CentralUS`, but the function is in East US)

### Root cause

The digest template renderer uses esbuild to compile JSX email templates at runtime. After deployment to Azure Functions (Linux), the `@esbuild/linux-x64` native binary at `node_modules/@esbuild/linux-x64/bin/esbuild` does not have execute permissions (`EACCES`).

### Relevant code

- `patchnotes-email/src/functions/sendDigest.ts` — lines 164-180 (template rendering + catch)
- `patchnotes-email/src/lib/templateRenderer.ts` — uses esbuild to compile JSX

### Fix applied

Replaced `esbuild` with `esbuild-wasm` — a pure WASM implementation with the same API that doesn't require native binary execution. All 47 tests pass.

### Remaining work

- Deploy the `esbuild-wasm` change
- Move `fn-patchnotes-email` from East US to Central US (same region as `api-myreleasenotes-ai`) so the `AppService.CentralUS` IP restriction can be restored for defense-in-depth

---

## Duplicate users created for the same Stytch account (race condition)

**Status:** Fixed

### Symptoms

- Two `Users` rows exist with the same `StytchUserId` but different `Id` values
- Both rows were created at the same time
- Watchlists, digest settings, and other data may be split across the two rows, causing inconsistent behavior (e.g., digest emails only query one of them)

### Root cause

There are two independent code paths that create users:

1. **Stytch webhook** (`PatchNotes.Api/Webhooks/StytchWebhook.cs:121-138`) — triggered by Stytch's `user.CREATE` event
2. **Login endpoint** (`PatchNotes.Api/Routes/UserRoutes.cs:57-68`) — called by the frontend after authentication

Both perform a read-then-write pattern:
```
user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchId);
if (user == null) { user = new User { ... }; db.Users.Add(user); }
await db.SaveChangesAsync();
```

When a user signs up, Stytch sends the webhook and the frontend calls the login endpoint nearly simultaneously. Both queries see `user == null` and both insert a new row. There is no unique constraint on `StytchUserId` in the database to prevent this.

### Relevant code

- `PatchNotes.Api/Webhooks/StytchWebhook.cs` — lines 121-138 (webhook user creation)
- `PatchNotes.Api/Routes/UserRoutes.cs` — lines 57-68 (login user creation)
- `PatchNotes.Data/` — User entity has no unique index on `StytchUserId`

### Fix applied

1. **Added unique index** on `Users.StytchUserId` via migration — the migration also merges any existing duplicate users (keeps the one with the most watchlist entries, reassigns related data)
2. **Login endpoint** (`UserRoutes.cs`) catches `DbUpdateException` on insert, detaches the failed entity, re-queries the existing user, and falls through to the update path
3. **Stytch webhook** (`StytchWebhook.cs`) same pattern — catches `DbUpdateException` on create, falls back to update
