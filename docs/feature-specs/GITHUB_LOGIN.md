# GitHub Login

Add GitHub as an OAuth login option via Stytch, and optionally import a user's watched repos into their PatchNotes watchlist.

## Why a GitHub App (not an OAuth App)

- GitHub Apps support the same OAuth 2.0 user authorization flow as OAuth Apps
- More granular permissions — users see exactly what's being requested
- GitHub recommends Apps over OAuth Apps for new integrations
- Builds user trust since we only need minimal access

Reference: https://docs.github.com/en/apps/creating-github-apps/setting-up-a-github-app/about-creating-github-apps

## Setup Steps

### 1. Create a GitHub App

Go to https://github.com/settings/apps/new and configure:

- **App name**: PatchNotes (or similar)
- **Homepage URL**: your production URL
- **Callback URL**:
  - Test: `https://test.stytch.com/v1/oauth/callback`
  - Production: `https://api.stytch.com/v1/oauth/callback`
- **Enable Device Flow**: No (web app only, no CLI flow needed)
- **Webhook**: Disable (not needed for OAuth login)
- **Permissions**: None initially (just name/email for login)

Save the **Client ID** and generate a **Client Secret**.

### 2. Configure Stytch

In the Stytch Dashboard:

1. Go to **Authentication → OAuth**
2. Enable **GitHub** as a provider
3. Enter the Client ID and Client Secret from the GitHub App

### 3. Update Frontend Login Config

In `patchnotes-web/src/auth/stytch.ts`:

- Add `Products.oauth` to the products array
- Add GitHub as an OAuth provider in the login config

The `StytchLogin` component will automatically render a "Continue with GitHub" button.

### 4. No Backend Changes Needed

The existing Stytch session validation and `/authenticate` callback route handle OAuth flows generically.

## Phase 2: Import Watched Repos

After login is working, add an optional "Import from GitHub" feature that pulls the user's watched repos into their PatchNotes watchlist.

### Why a Separate Step

- Initial login requests only name/email — minimal permissions, builds trust
- Repo import requires additional GitHub permissions (Metadata read-only at minimum)
- Users opt in to the expanded scope only when they want the convenience

### How It Works (Incremental Authorization via Stytch)

Stytch's GitHub OAuth start endpoint supports a `custom_scopes` parameter, which lets you request additional GitHub API scopes beyond what was granted at initial login. This means you don't need a separate OAuth integration — just trigger a new OAuth round with expanded scopes.

**Flow:**

1. User clicks "Import from GitHub" in their watchlist
2. App initiates a new Stytch OAuth flow via `/oauth/github/start` with `custom_scopes` set to the repo metadata scope (URL-encoded, spaces as `%20`)
3. GitHub shows a consent screen for the additional permission only
4. GitHub redirects back through Stytch as normal
5. Stytch merges the new OAuth factor into the user's existing session via `session_jwt` — no new login required
6. App receives an access token with the expanded scopes
7. App calls `GET /user/subscriptions` to fetch watched repos
8. User selects which repos to add to their watchlist

**References:**

- [Stytch GitHub OAuth Start](https://stytch.com/docs/api/oauth-github-start) — documents `custom_scopes` parameter
- [Stytch OAuth Authenticate](https://stytch.com/docs/api/oauth-authenticate) — session merging via `session_jwt`

### Why Watched Over Starred

- **Watched** (`/user/subscriptions`) — repos they actively follow for notifications, usually a curated list
- **Starred** (`/user/starred`) — bookmarked repos, often a much larger/noisier list

Watched maps better to "repos I care about release notes for."

### Permissions Required

On the GitHub App, enable **Metadata (read-only)** repository permission for this phase. Users will see the app can read repo metadata — still no write access.
