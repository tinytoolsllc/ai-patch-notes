# Codeberg (Forgejo/Gitea) Release Support

Add support for tracking releases from Codeberg repositories, enabling PatchNotes to ingest release data from projects hosted on Codeberg (and potentially other Forgejo/Gitea instances in the future).

## Motivation

PatchNotes currently only supports GitHub-hosted repositories. Codeberg is a growing, community-run Forgejo instance hosting notable open-source projects (e.g., `ampmod/ampmod`, `ordinarylabs/Ordinary`). Supporting Codeberg broadens the pool of trackable packages and opens the door to other Gitea/Forgejo forges.

## Background: Codeberg API

Codeberg runs Forgejo (a Gitea hard-fork). Its REST API lives at `https://codeberg.org/api/v1/` and closely mirrors the Gitea API. Key details for release ingestion:

| Aspect | Detail |
|--------|--------|
| **List releases** | `GET /api/v1/repos/{owner}/{repo}/releases` |
| **Pagination** | `page` (1-indexed) + `limit` (default 30, max 50). `Link` header + `X-Total-Count` |
| **Auth (public repos)** | Not required |
| **Auth (private/drafts)** | `Authorization: token <pat>` |
| **Rate limit** | ~2000 req / 5 min (HAProxy-level, no `X-RateLimit-*` headers) |
| **Release fields** | `id`, `tag_name`, `name`, `body`, `draft`, `prerelease`, `published_at`, `assets[]`, `author` |

The release object is structurally similar to GitHub's — `tag_name`, `name`, `body`, `draft`, `prerelease`, `published_at` all map directly.

## Design

### Phase 1: Introduce a Forge Provider Abstraction

The current architecture hardcodes GitHub throughout. Before adding Codeberg, we need a provider abstraction so `SyncService` can work with any forge.

#### 1a. `ForgeType` Enum

```csharp
// PatchNotes.Data/ForgeType.cs
public enum ForgeType
{
    GitHub = 0,
    Codeberg = 1,
    // Future: Gitea = 2, GitLab = 3
}
```

#### 1b. Extend `Package` Model

Add a `ForgeType` column and a generic `ForgeUrl` field. Keep `GithubOwner`/`GithubRepo` for backwards compatibility — new packages will also populate `ForgeType`, `ForgeOwner`, `ForgeRepo`.

```csharp
public class Package
{
    // ... existing fields ...
    public ForgeType ForgeType { get; set; } = ForgeType.GitHub;
    public string? ForgeOwner { get; set; }  // nullable during migration; copied from GithubOwner
    public string? ForgeRepo { get; set; }   // nullable during migration; copied from GithubRepo
}
```

Migration: backfill `ForgeType = GitHub`, `ForgeOwner = GithubOwner`, `ForgeRepo = GithubRepo` for all existing rows.

After backfill is verified in production, a follow-up migration can drop the `GithubOwner`/`GithubRepo` columns and make `ForgeOwner`/`ForgeRepo` non-nullable.

#### 1c. `IForgeClient` Interface

Extract a common interface from `IGitHubClient`:

```csharp
// PatchNotes.Sync.Core/Forge/IForgeClient.cs
public interface IForgeClient
{
    IAsyncEnumerable<ForgeRelease> GetAllReleasesAsync(
        string owner, string repo, CancellationToken ct = default);

    Task<ForgeRelease?> GetReleaseByTagAsync(
        string owner, string repo, string tag, CancellationToken ct = default);

    Task<string?> GetFileContentAsync(
        string owner, string repo, string path, CancellationToken ct = default);
}
```

`ForgeRelease` is a normalized DTO shared across forges:

```csharp
public record ForgeRelease(
    long Id,
    string TagName,
    string? Name,
    string? Body,
    bool Draft,
    bool Prerelease,
    DateTimeOffset? PublishedAt,
    string HtmlUrl);
```

#### 1d. `IForgeClientFactory`

```csharp
public interface IForgeClientFactory
{
    IForgeClient GetClient(ForgeType forgeType);
}
```

`SyncService` replaces its `IGitHubClient` dependency with `IForgeClientFactory` and resolves the correct client per-package.

#### 1e. Adapt `GitHubClient`

`GitHubClient` implements `IForgeClient` (in addition to keeping `IGitHubClient` for any GitHub-specific methods like `SearchRepositoriesAsync`). The mapping from `GitHubRelease` → `ForgeRelease` is trivial.

### Phase 2: Codeberg Client Implementation

#### 2a. `CodebergClient`

```
PatchNotes.Sync.Core/Codeberg/
  CodebergClient.cs          — implements IForgeClient
  Models/
    CodebergRelease.cs       — Gitea/Forgejo release JSON DTO
  CodebergOptions.cs         — base URL, optional PAT
  CodebergServiceCollectionExtensions.cs
```

Key implementation notes:

- **Base URL**: `https://codeberg.org/api/v1` (configurable via `CodebergOptions` to support self-hosted Forgejo/Gitea later).
- **Pagination**: Follow `Link` header `rel="next"` or iterate until an empty page. Max `limit=50` per request.
- **Rate limiting**: No API headers to inspect. Implement a simple sliding-window tracker (2000 req / 5 min) and back off proactively. Log warnings when approaching the threshold.
- **Resilience**: Same retry policy as `GitHubClient` (3 retries, exponential backoff on 429/5xx).
- **Auth**: Optional `Authorization: token <pat>` header. Public repos don't need it, but auth gives access to drafts and private repos.

#### 2b. DTO Mapping

Codeberg response → `ForgeRelease` mapping:

| Codeberg field | ForgeRelease field |
|---|---|
| `id` | `Id` |
| `tag_name` | `TagName` |
| `name` | `Name` |
| `body` | `Body` |
| `draft` | `Draft` |
| `prerelease` | `Prerelease` |
| `published_at` | `PublishedAt` |
| `html_url` | `HtmlUrl` |

#### 2c. Configuration

```json
// appsettings.json
{
  "Codeberg": {
    "BaseUrl": "https://codeberg.org/api/v1",
    "Token": ""  // optional, for private repos
  }
}
```

### Phase 3: URL Parsing & Package Creation

#### 3a. `ForgeUrlParser`

Extend or replace `GitHubUrlParser` to detect the forge from a URL:

| URL pattern | Detected forge |
|---|---|
| `github.com/{owner}/{repo}` | GitHub |
| `codeberg.org/{owner}/{repo}` | Codeberg |

This is used when adding packages via the admin UI or API. The parser returns `(ForgeType, Owner, Repo)`.

#### 3b. Update Add-Package Flow

The `POST /api/packages` endpoint (and any admin UI) should:
1. Accept a repository URL (not just GitHub owner/repo).
2. Use `ForgeUrlParser` to detect forge type.
3. Populate `ForgeType`, `ForgeOwner`, `ForgeRepo` on the new `Package`.

### Phase 4: Changelog Resolution

`ChangelogResolver` currently uses `IGitHubClient.GetFileContentAsync` to fetch `CHANGELOG.md` from GitHub. It needs to use `IForgeClientFactory` to get the correct client for the package's forge. The `IForgeClient.GetFileContentAsync` method covers this.

Codeberg equivalent API: `GET /api/v1/repos/{owner}/{repo}/raw/{filepath}`

### Phase 5: Web Frontend

#### 5a. Package Display

- Show forge icon (GitHub octocat vs Codeberg logo) on package cards.
- Link to correct forge URL (`https://codeberg.org/{owner}/{repo}`).

#### 5b. Add Package Form

- Accept a full repository URL instead of separate owner/repo fields.
- Auto-detect forge type from the URL.
- Show the detected forge as a badge/icon.

## Migration Strategy

1. **Database migration**: Add `ForgeType`, `ForgeOwner`, `ForgeRepo` columns. Backfill from existing `GithubOwner`/`GithubRepo`.
2. **Deploy abstraction**: Ship `IForgeClient` + factory with only `GitHubClient` registered. Verify nothing breaks.
3. **Add Codeberg**: Register `CodebergClient`, enable adding Codeberg packages.
4. **Cleanup** (follow-up): Deprecate and eventually remove `GithubOwner`/`GithubRepo` columns.

## Out of Scope

- **Self-hosted Gitea/Forgejo instances**: The architecture supports it (configurable base URL), but discovery/onboarding UX is deferred.
- **GitLab support**: Different API shape; separate feature spec.
- **Codeberg repo search**: Gitea's search API is less mature than GitHub's. Package discovery stays manual for now.
- **Codeberg OAuth login**: Separate concern from release ingestion.

## Open Questions

- [ ] Should we support multiple Gitea/Forgejo instances from day one (configurable base URL list), or hardcode Codeberg initially?
- [ ] Do we need a PAT for any Codeberg packages we plan to track, or are they all public?
- [ ] Should the `ForgeOwner`/`ForgeRepo` replacement happen in the same release, or is a two-phase migration safer given production data?

## References

- [Forgejo API docs](https://forgejo.org/docs/next/user/api-usage/)
- [Gitea API: List Releases](https://gitea.com/api/swagger#/repository/repoListReleases)
- [Codeberg API Swagger](https://codeberg.org/swagger.json)
- [Codeberg rate limits (Community #425)](https://codeberg.org/Codeberg/Community/issues/425)
