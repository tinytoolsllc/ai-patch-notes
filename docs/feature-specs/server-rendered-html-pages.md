# Server-Rendered HTML Pages for SEO Routes

## Context
Pages linked from sitemap.xml (`/packages/{owner}/{repo}`, `/releases/{id}`, `/packages/{owner}`) are slow because they go through the full SPA bootstrap. We'll serve pre-rendered HTML directly from the C# API for these routes.

## Architecture

### Routing layer

We need a proxy in front of `www.myreleasenotes.ai` that routes `/packages/*` and `/releases/*` to the C# API while everything else goes to the SWA. The proxy must be transparent (no redirects - the user's URL stays the same).

#### Why Azure SWA route rules don't work

Azure Static Web Apps `rewrite` only supports **paths relative to the app root** (local files). It cannot rewrite to external URLs like `https://api.myreleasenotes.ai/...`. The linked backend feature only proxies routes starting with `/api` and the prefix is not configurable.

Sources: [SWA Configuration docs](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration), [SWA App Service API docs](https://learn.microsoft.com/en-us/azure/static-web-apps/apis-app-service)

#### Option A: Cloudflare Cloud Connector (recommended)

Dashboard-based rule builder, no code required. Matches requests by URI path and transparently proxies to a different origin. Free on all plans (up to 10 rules on free tier).

Two rules needed:
- `/packages/*` → `api.myreleasenotes.ai`
- `/releases/*` → `api.myreleasenotes.ai`

All cookie/auth logic stays in the C# API - the connector is just a dumb path router.

- **Prerequisite**: Domain DNS must be on Cloudflare

#### Option B: Cloudflare Worker (fallback if Cloud Connector has limitations)

A small Worker script that does the same path-based routing in code. Only needed if Cloud Connector can't handle cookie forwarding or has other issues with proxying to an App Service.

- **Free tier**: 100k requests/day (~3M/month)
- **Paid**: $5/month for 10M requests

Sources: [Cloud Connector](https://blog.cloudflare.com/cloud-connector/), [Workers routing](https://developers.cloudflare.com/workers/configuration/routing/routes/), [Workers pricing](https://developers.cloudflare.com/workers/platform/pricing/)

### Request flow (cookie-based branching)
The C# endpoint checks for the Stytch session cookie on each request:

**No cookie (bot or anonymous user):**
→ Return full server-rendered HTML with content inline. Fast, SEO-friendly, complete page.

**Has cookie (authenticated user):**
→ Return a lightweight HTML page that:
1. Shows a spinner immediately (instant visual feedback)
2. Embeds the API response as `window.__PRELOADED_DATA__` in a `<script>` tag
3. Loads the SPA bundle in the background
4. SPA boots, reads preloaded data into React Query cache (no re-fetch), renders the full page

This avoids the cloaking problem (not discriminating by bot vs human, but by auth state) and gives authenticated users a seamless transition to the full SPA with preloaded data.

## Pages to render

1. **`GET /packages/{owner}/{repo}`** - Package detail with version groups and AI summaries
2. **`GET /releases/{releaseId}`** - Single release with markdown body
3. **`GET /packages/{owner}`** - Owner package listing

## Server-rendered HTML (no-cookie path)

Each page includes:
- `<head>`: charset, viewport, title, meta description, Open Graph tags, JSON-LD schema (same SEO data the SPA generates in route `head()` functions)
- `<style>`: Inline CSS with the theme variables and component styles
- `<body>`: Static header (logo + breadcrumbs, no auth UI), page content, footer
- Markdown rendering for release bodies using [Markdig](https://github.com/xoofx/markdig)

No auth UI, no interactive elements, no SPA bundle. Just content.

### Dark mode support (both paths)

Both the server-rendered and authenticated HTML pages include an inline script in `<head>` (before CSS) that reads the theme from localStorage and applies the correct class to `<html>` to prevent a flash of wrong theme:

```html
<script>
  try {
    var d = JSON.parse(localStorage.getItem('patchnotes-theme'));
    var t = d && d.state && d.state.theme;
    var r = t === 'dark' ? 'dark' : t === 'light' ? 'light'
      : window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    document.documentElement.classList.add(r);
  } catch(e) {}
</script>
```

This matches the Zustand persist store key (`patchnotes-theme`) and the `applyTheme()` logic in `themeStore.ts`.

## Authenticated HTML (cookie path)

A minimal HTML page with:
- Same `<head>` (SEO tags, CSS)
- A centered spinner in the `<body>`
- `<script>window.__PRELOADED_DATA__ = { /* package/release JSON */ }</script>`
- `<script type="module" src="/assets/main-xxxx.js"></script>` (SPA entry point from the SWA)

The SPA boots, finds `window.__PRELOADED_DATA__`, seeds the React Query cache, and renders the full interactive page without an API round-trip.

### Frontend changes for preloaded data
In the SPA, the route loaders check for `window.__PRELOADED_DATA__` before fetching:
```typescript
// In queryClient setup or route loader
if (window.__PRELOADED_DATA__) {
  queryClient.setQueryData(queryKey, window.__PRELOADED_DATA__)
  delete window.__PRELOADED_DATA__
}
```

## Shared template helper

`HtmlTemplate` static class with:
- `Wrap(title, description, path, bodyHtml, jsonLd?)` - full HTML document with head/style/footer
- `WrapAuthenticated(title, path, preloadedDataJson, spaEntryUrl)` - spinner + preloaded data + SPA script
- `Header(breadcrumbs)` - static header with logo and breadcrumb links
- `Footer()` - footer
- CSS constant with theme variables and component styles

## Files to create/modify

- `PatchNotes.Api/Routes/HtmlPageRoutes.cs` - new: the 3 HTML endpoints with cookie branching
- `PatchNotes.Api/Routes/HtmlTemplate.cs` - new: shared HTML template helpers
- `PatchNotes.Api/PatchNotes.Api.csproj` - add Markdig package reference
- `PatchNotes.Api/Program.cs` - register `app.MapHtmlPageRoutes()`
- `PatchNotes.Tests/HtmlPageTests.cs` - new: integration tests
- Cloudflare Worker script (or Cloud Connector rule) - route proxy
- `patchnotes-web/src/queryClient.ts` or route loaders - read `window.__PRELOADED_DATA__`

### Key files to reference
- `PatchNotes.Api/Routes/PackageRoutes.cs` lines 183-259 - package detail query
- `PatchNotes.Api/Routes/ReleaseRoutes.cs` - release detail query
- `PatchNotes.Api/Routes/SitemapRoutes.cs` - URL patterns
- `PatchNotes.Api/Routes/StatusPageRoutes.cs` - existing HTML rendering pattern
- `patchnotes-web/src/index.css` - theme variables to replicate
- `patchnotes-web/src/pages/PackageDetailByRepo.tsx` - page layout reference
- `patchnotes-web/src/pages/ReleaseDetail.tsx` - page layout reference

## Route conflict handling

The API already has `GET /api/packages/{owner}/{repo}` (JSON). The new HTML routes are at `/packages/{owner}/{repo}` (no `/api` prefix), so no conflict.

## Tests

Integration tests in `HtmlPageTests.cs`:
- **No-cookie path**: returns 200 with `text/html`, content includes package name / release tag / summary
- **Cookie path**: returns 200 with `text/html`, includes spinner markup and `window.__PRELOADED_DATA__`
- 404 for non-existent packages/releases
- Markdown in release body is rendered to HTML
- SEO meta tags and JSON-LD are present in no-cookie response

## Verification
1. `dotnet test` - all tests pass
2. `curl localhost:2101/packages/facebook/react` - returns full HTML with content (no cookie)
3. `curl -H "Cookie: stytch_session=xxx" localhost:2101/packages/facebook/react` - returns spinner + preloaded data
4. View source shows proper meta tags, JSON-LD, and styled content
5. SPA picks up preloaded data without re-fetching
