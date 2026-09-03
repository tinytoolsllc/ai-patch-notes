# Server-Rendered HTML Pages for SEO Routes

## Context

Pages linked from sitemap.xml (`/packages/{owner}/{repo}`, `/releases/{id}`, `/packages/{owner}`) are slow because they go through the full SPA bootstrap. We serve pre-rendered HTML directly from the C# API for these routes.

## Architecture

### Routing: Cloudflare Worker

A Cloudflare Worker intercepts requests to `/packages/*` and `/releases/*` on `www.myreleasenotes.ai`. It checks for the Stytch session cookie:

- **No cookie (bot or anonymous user)**: proxies to `api.myreleasenotes.ai/html/...` for server-rendered HTML
- **Has cookie (authenticated user)**: passes through to the SWA for the full SPA experience

The Worker script lives in `cloudflare-worker/worker.js`. Worker Routes are configured in the Cloudflare dashboard:

- `www.myreleasenotes.ai/packages/*`
- `www.myreleasenotes.ai/releases/*`

**Free tier**: 100k requests/day (~3M/month). **Prerequisite**: Domain DNS must be proxied through Cloudflare.

#### Why Azure SWA route rules don't work

Azure Static Web Apps `rewrite` only supports paths relative to the app root (local files). It cannot rewrite to external URLs. The linked backend feature only proxies routes starting with `/api` and the prefix is not configurable. Cloudflare Origin Rules (which can change the origin hostname) require an Enterprise plan.

Sources: [SWA Configuration docs](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration), [SWA App Service API docs](https://learn.microsoft.com/en-us/azure/static-web-apps/apis-app-service)

## API endpoints

The C# API serves server-rendered HTML at `/html/*`. These always return full HTML regardless of cookies (the Worker handles the cookie branching).

1. **`GET /html/packages/{owner}/{repo}`** - Package detail with version groups, AI summaries, and hero card
2. **`GET /html/releases/{id}`** - Single release with Markdig-rendered markdown body
3. **`GET /html/packages/{owner}`** - Owner package listing

### HTML structure

Each page includes:

- `<head>`: charset, viewport, title, meta description, Open Graph tags, JSON-LD schema, canonical URL
- `<script>`: dark mode detection from localStorage (matches `patchnotes-theme` Zustand store)
- `<style>`: inline CSS with theme variables and component styles (matches the SPA's Tailwind theme)
- `<body>`: static header (SVG logo + breadcrumbs + sign-in button), page content, footer
- Package detail pages include a hero/marketing card wrapped in `data-nosnippet` (excluded from search snippets)
- Markdown rendering via [Markdig](https://github.com/xoofx/markdig) for release bodies and AI summaries

## Files

- `cloudflare-worker/worker.js` - Cloudflare Worker script (cookie check + path rewriting)
- `PatchNotes.Api/Routes/HtmlPageRoutes.cs` - the 3 HTML endpoints
- `PatchNotes.Api/Routes/HtmlTemplate.cs` - shared HTML template (head, CSS, header, footer, hero card)
- `PatchNotes.Tests/HtmlPageTests.cs` - integration tests

## Tests

Integration tests in `HtmlPageTests.cs`:

- Returns 200 with `text/html` content type for each page
- HTML contains expected content (package name, release tag, summary text)
- 404 for non-existent packages/releases
- Markdown in release body is rendered to HTML
- SEO meta tags and JSON-LD are present
- Hero card with `data-nosnippet` is present on package detail pages
- Theme detection script is present

## Verification

1. `dotnet test` - all tests pass
2. `curl https://www.myreleasenotes.ai/packages/facebook/react` - returns full HTML (anonymous, via Worker)
3. Browse with Stytch cookie - gets the SPA (Worker passes through to SWA)
4. View source shows proper meta tags, JSON-LD, and styled content
