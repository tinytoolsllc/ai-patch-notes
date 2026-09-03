# Preload All Packages for Fast Homepage Search

Preload the full package catalog when the homepage loads, enabling instant client-side search without round-trips to the server.

## Motivation

There's no way to search or discover packages from the homepage today. The feed shows a small subset (5 groups for anonymous users, watchlist for logged-in users), but users can't browse or find specific packages. Preloading the full catalog (~138 packages, ~10-15 KB gzipped) makes instant search trivial with zero perceived latency.

## Design

### Backend: Unpaginated Package Catalog Endpoint

Add a lightweight, cacheable endpoint that returns all packages in a single response.

```
GET /api/packages/catalog
```

**Response:**

```json
{
  "packages": [
    {
      "id": "abc123...",
      "name": "react",
      "npmName": "react",
      "githubOwner": "facebook",
      "githubRepo": "react",
      "url": "https://github.com/facebook/react"
    }
  ],
  "total": 138
}
```

**Why a new endpoint instead of `GET /api/packages?limit=999`:**

- The existing `GET /api/packages` returns `PackageDto` with fields like `tagPrefix`, `lastFetchedAt`, `createdAt` that are irrelevant for search and waste bytes.
- A dedicated DTO keeps the payload minimal — only the fields needed for display and search.
- Easier to cache aggressively (packages change rarely).

**Caching:** 5-minute `IMemoryCache` on the server. Packages are added/removed infrequently, so staleness is acceptable. Add `Cache-Control: public, max-age=300` header so CDN/browser can cache too.

**DTO:**

```csharp
public class PackageCatalogItemDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? NpmName { get; set; }
    public required string GithubOwner { get; set; }
    public required string GithubRepo { get; set; }
    public string? Url { get; set; }
}

public class PackageCatalogResponseDto
{
    public required List<PackageCatalogItemDto> Packages { get; set; }
    public int Total { get; set; }
}
```

### Frontend: Preload in Route Loader

Add a parallel `ensureQueryData` call in the index route loader, alongside the existing feed preload:

```typescript
// routes/index.tsx
export const Route = createFileRoute("/")({
  loader: ({ context: { queryClient } }) => {
    // Fire both in parallel — no await, just prime the cache
    queryClient.ensureQueryData(getGetFeedQueryOptions({ excludePrerelease: true }));
    queryClient.ensureQueryData(getGetPackageCatalogQueryOptions());
  },
});
```

Both requests fire concurrently before the component mounts. TanStack Query caches the catalog, so subsequent navigations to `/` are instant.

### Frontend: Search Component

Add a search input to the homepage filter bar (alongside the existing prerelease/sort controls):

```
┌─────────────────────────────────────────────────┐
│  [🔍 Search packages...]   [filters] [sort]    │
│                                                  │
│  Recently Updated Packages                       │
│  ┌─────────────────────────────────────────┐    │
│  │ react v19.x — ...                       │    │
│  └─────────────────────────────────────────┘    │
│  ...                                             │
└─────────────────────────────────────────────────┘
```

**Behavior:**

- Empty input: show the feed as today (default feed or watchlist).
- Typing: filter the preloaded catalog client-side, show matching packages as a dropdown/results list below the input. Match against `name`, `npmName`, `githubOwner/githubRepo`.
- Selecting a result: navigate to the package detail page (`/packages/{owner}/{repo}`).
- No debounce needed — filtering ~138 items in memory is sub-millisecond.

**Search matching:** Case-insensitive substring match across `name`, `npmName`, and `githubOwner/githubRepo`. Simple and sufficient at this scale. Fuzzy matching (e.g., Fuse.js) is overkill for <500 items but could be added later.

### Scaling Threshold

This approach works well up to ~2,000-5,000 packages. Beyond that, the payload size (~400 KB+) and client-side filtering cost become noticeable on low-end devices. At that point, switch to a debounced server-side search endpoint. Current catalog is 138 packages — 15-35x runway before this becomes a concern.

## Implementation Steps

### Backend

- [ ] Add `PackageCatalogItemDto` and `PackageCatalogResponseDto`
- [ ] Add `GET /api/packages/catalog` endpoint in `PackageRoutes.cs`
- [ ] Add 5-minute `IMemoryCache` + `Cache-Control` response header
- [ ] Run Orval to generate the React Query hook

### Frontend

- [ ] Add `ensureQueryData` for catalog in `routes/index.tsx` loader
- [ ] Add search input component to `HomePage.tsx` filter bar
- [ ] Implement client-side filtering against cached catalog
- [ ] Navigate to package detail on result selection
- [ ] Handle empty state ("No packages match...")

## Out of Scope

- Full-text search across release notes/summaries (server-side concern, separate feature)
- Fuzzy matching / typo tolerance (not needed at current scale)
- Search analytics / popular searches
- Keyboard navigation in search results (nice-to-have follow-up)
