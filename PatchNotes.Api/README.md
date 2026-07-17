# PatchNotes.Api

ASP.NET Core Web API for the PatchNotes application.

## Overview

This project provides the REST API for managing packages, releases, summaries, feed, watchlists, subscriptions, and user authentication via Stytch.

## Running

```bash
dotnet run
```

The API runs on `http://localhost:5031` by default.

## API Endpoints

### Packages

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/packages` | List all tracked packages | No |
| GET | `/api/packages/{id}` | Get package details | No |
| GET | `/api/packages/{id}/releases` | Get releases for a package | No |
| GET | `/api/packages/{owner}` | Get packages by GitHub owner | No |
| GET | `/api/packages/{owner}/{repo}` | Get package by owner/repo | No |
| POST | `/api/packages` | Add a new package | Yes |
| PATCH | `/api/packages/{id}` | Update package GitHub mapping | Yes |
| DELETE | `/api/packages/{id}` | Remove a package | Yes |

### Releases

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/releases` | Query releases (supports `packages`, `days`, `excludePrerelease`, `majorVersion` params) | No |
| GET | `/api/releases/{id}` | Get release details | No |

### Summaries

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/packages/{id}/summaries` | Get summaries for a package | No |
| GET | `/api/summaries` | Get all summaries (supports `includePrerelease`, `majorVersion` params) | No |

### Feed

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/feed` | Combined feed with server-side grouping (supports `excludePrerelease`) | No |

### Users

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/users/me` | Get current authenticated user | Yes |
| POST | `/api/users/login` | Create/update user on login | Yes |
| PUT | `/api/users/me` | Update user profile | Yes |
| GET | `/api/users/me/email-preferences` | Get email preferences | Yes |
| PATCH | `/api/users/me/email-preferences` | Update email preferences | Yes |

### Watchlist

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/watchlist` | Get current user's watched package IDs | Yes |
| PUT | `/api/watchlist` | Replace entire watchlist (bulk set) | Yes |
| POST | `/api/watchlist/{packageId}` | Add a package to watchlist | Yes |
| DELETE | `/api/watchlist/{packageId}` | Remove a package from watchlist | Yes |

### Subscriptions

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/subscription/checkout` | Create Stripe Checkout session | Yes |
| POST | `/api/subscription/portal` | Create Stripe Customer Portal session | Yes |
| GET | `/api/subscription/status` | Get current subscription status | Yes |

### Webhooks

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/webhooks/stytch` | Handle Stytch webhook events (Svix signature verified) | No |
| POST | `/webhooks/stripe` | Handle Stripe webhook events | No |

### Admin

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/admin/packages/health` | Package sync health info | Admin |
| POST | `/api/admin/packages/{id}/reset-sync` | Reset failure tracking | Admin |
| POST | `/api/admin/packages/{id}/disable-sync` | Disable sync for a package | Admin |
| POST | `/api/admin/packages/{id}/reset-summaries` | Mark releases stale and delete summaries | Admin |
| POST | `/api/admin/packages/{id}/reset-releases` | Delete all releases/summaries, reset sync | Admin |
| POST | `/api/packages/bulk` | Bulk add packages | Admin |
| GET | `/api/admin/github/search` | Search GitHub repositories | Admin |

### HTML Pages (Server-Rendered)

Pre-rendered HTML pages for SEO routes. A Cloudflare Worker (`cloudflare-worker/worker.js`) intercepts `/packages/*` and `/releases/*` on `www.myreleasenotes.ai`, checks for a Stytch session cookie, and routes anonymous users/bots to these API endpoints. Authenticated users are passed through to the SPA.

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/html/packages/{owner}` | Owner package listing | No |
| GET | `/html/packages/{owner}/{repo}` | Package detail with version groups and summaries | No |
| GET | `/html/releases/{id}` | Release detail with rendered markdown | No |

### Status

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/` | Status page | No |

## Configuration

Configuration is loaded from `appsettings.json` and environment variables:

- `AI:BaseUrl` - AI provider base URL (default: `https://ollama.com/v1/`)
- `AI:ApiKey` - AI provider API key
- `AI:Model` - AI model name (default: `gemma4:31b`)
- `Stytch:ProjectId` - Stytch project ID
- `Stytch:Secret` - Stytch secret
- `Stytch:WebhookSecret` - Secret for webhook signature verification
- `Stripe:SecretKey` - Stripe secret key
- `Stripe:WebhookSecret` - Stripe webhook secret
- `Stripe:PriceId` - Stripe price ID for Pro subscription

## Directory Structure

```
PatchNotes.Api/
├── Routes/
│   ├── PackageRoutes.cs      # Package CRUD + owner/repo endpoints
│   ├── ReleaseRoutes.cs      # Release queries
│   ├── SummaryRoutes.cs      # AI summary endpoints
│   ├── FeedRoutes.cs         # Combined feed endpoint
│   ├── UserRoutes.cs         # User profile + email preferences
│   ├── WatchlistRoutes.cs    # Watchlist management
│   ├── SubscriptionRoutes.cs # Stripe subscription endpoints
│   ├── HtmlPageRoutes.cs     # Server-rendered HTML for SEO routes
│   ├── HtmlTemplate.cs       # Shared HTML template (head, CSS, header, footer)
│   ├── StatusPageRoutes.cs   # Status page
│   ├── SitemapRoutes.cs      # Sitemap XML generation
│   └── RouteUtils.cs         # Shared utilities (auth, URL parsing)
├── Stytch/
│   ├── StytchClient.cs       # Stytch API client
│   └── IStytchClient.cs      # Client interface
├── Webhooks/
│   ├── StytchWebhook.cs      # Stytch webhook handler
│   └── StripeWebhook.cs      # Stripe webhook handler
├── Program.cs                # Application entry point
└── appsettings.json          # Configuration
```

## Dependencies

- **PatchNotes.Data** - Data layer and entity models
- **Stripe.net** - Stripe payments
- **Stytch.net** - User authentication
- **Svix** - Webhook signature verification
- **Markdig** - Markdown to HTML rendering (for server-rendered pages)
