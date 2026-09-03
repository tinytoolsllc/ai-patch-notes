# PatchNotes Roadmap

## Branding

- [ ] Rebrand to **"AI Patch Notes by Tiny Tools"**

---

## Pre-commit Hook

- [ ] Fix pre-commit lint configuration - should only run full lint when JS/TS files are staged

---

## Open PRs (Ready to Merge)

- [x] **PR #32** - Split API routes into separate files _(Merged)_
- [ ] **PR #33** - Clean up Data project structure
- [ ] **PR #34** - Split Sync project into separate files

---

## Epic: Remove Notifications

> **Status: Not Yet Implemented**

Remove all notification-related code from the codebase. Notifications feature is being deprecated.

### Feature: Backend Cleanup

- [ ] Delete `Notification.cs` entity
- [ ] Remove `Notifications` DbSet from `PatchNotesDbContext`
- [ ] Remove notification endpoints from API (`/api/notifications/*`)
- [ ] Delete `NotificationSyncService.cs`
- [ ] Remove `SyncNotificationsAsync` from `SyncService.cs`
- [ ] Remove `NotificationSyncResult` from `SyncResults.cs`
- [ ] Remove notification registration from `Program.cs` (Sync project)
- [ ] Remove GitHub notification client methods (`GetAllNotificationsAsync`)
- [ ] Remove GitHub notification DTOs (`GitHubNotification`, `GitHubNotificationSubject`, etc.)
- [ ] Delete notification-related tests from `SyncServiceTests.cs`
- [ ] Create migration to drop `Notifications` table

### Feature: Frontend Cleanup

- [ ] Remove notification hooks from `hooks.ts` (`useNotifications`, `useNotificationsUnreadCount`, `useMarkNotificationAsRead`, `useDeleteNotification`)
- [ ] Remove `Notification` and `UnreadCount` types from `types.ts`
- [ ] Remove notification query keys from `hooks.ts`
- [ ] Remove notification mock handlers from `handlers.ts`
- [ ] Remove notification settings section from `SettingsModal.tsx`

---

## Epic: Sync Refactor

> **Status: Not Yet Implemented**

Refactor the sync tool to intelligently fetch releases and generate grouped summaries using AI (ollama/cloud).

### Feature: Release Fetching

- [ ] Fetch new releases for each package based on `LastFetchedAt`
- [ ] Detect releases that are new or missing summaries
- [ ] Track which releases need summary generation

### Feature: External Changelog Resolution

Handle releases that link to external changelog files instead of containing release notes directly (e.g., Vite's "Please refer to CHANGELOG.md for details" pattern).

- [ ] Detect minimal release bodies that reference external files (CHANGELOG.md, HISTORY.md, etc.)
- [ ] Parse changelog link from release body (supports relative and absolute GitHub URLs)
- [ ] Fetch external changelog file content from GitHub API
- [ ] Extract relevant version section from changelog (match version tag to changelog heading)
- [ ] Handle common changelog formats (Keep a Changelog, conventional commits, custom formats)
- [ ] Cache fetched changelog content to avoid redundant requests
- [ ] Fallback gracefully if changelog fetch/parse fails (keep original release body)

### Feature: Version Grouping

- [ ] Parse semantic versions from release tags
- [ ] Group releases by major version (e.g., 15.x, 16.x)
- [ ] Separate pre-release versions (alpha, beta, canary, rc, next, nightly) from stable
- [ ] Handle edge cases (non-semver tags, monorepo tags like `@package/v1.0.0`)
- [ ] Example: next.js 15.1, 16.1, 16.2-canary → 3 groups (15.x stable, 16.x stable, 16.x canary)

### Feature: Summary Generation

- [ ] Create `ReleaseSummary` entity (packageId, majorVersion, isPrerelease, summary, generatedAt)
- [ ] Add migration for `ReleaseSummary` table
- [ ] Generate summary per version group using AI (ollama local / cloud fallback)
- [ ] Aggregate release notes within a group for context
- [ ] Update summaries when new releases are added to a group
- [ ] Add configuration for AI provider (ollama URL, cloud API key)

### Feature: API Updates

- [ ] Add `GET /api/packages/{id}/summaries` endpoint (get all summaries for a package)
- [ ] Add `GET /api/summaries` endpoint (query summaries across packages)
- [ ] Include summary data in release responses where applicable

---

## Epic: User Settings

> **Status: Not Yet Implemented**

Persist user preferences via API.

### Feature: Backend

- [ ] Create `UserSettings` entity (userId, showPrereleases, compactView, etc.)
- [ ] Add migration for `UserSettings` table
- [ ] Add `GET /api/users/me/settings` endpoint
- [ ] Add `PATCH /api/users/me/settings` endpoint

### Feature: Frontend

- [ ] Create settings API hooks (`useUserSettings`, `useUpdateUserSettings`)
- [ ] Wire up `SettingsModal.tsx` to fetch/save settings
- [ ] Apply settings to release display (prerelease filter, compact view)
- [ ] Handle unauthenticated users (localStorage fallback or disable settings)

---

## Epic: User Watchlist

> **Status: Not Yet Implemented**

Allow users to maintain a personal watchlist of packages they want to track. Users watch packages (e.g., "react"), not specific versions - watching a package includes all major versions (v15, v16, v17, etc.).

### Feature: Data Model

- [ ] Ensure `Package` represents the npm package (e.g., "react"), not a specific version
- [ ] Releases link to packages and contain version info in `Tag` field
- [ ] Create `UserWatchlist` junction table (userId, packageId, addedAt)
- [ ] Add migration for `UserWatchlist` table

### Feature: Backend

- [ ] Add `GET /api/users/me/watchlist` endpoint (get user's watched packages)
- [ ] Add `POST /api/users/me/watchlist/{packageId}` endpoint (add to watchlist)
- [ ] Add `DELETE /api/users/me/watchlist/{packageId}` endpoint (remove from watchlist)
- [ ] Update `GET /api/releases` to support `?watchlist=true` filter for authenticated users

### Feature: Frontend

- [ ] Create watchlist API hooks (`useWatchlist`, `useAddToWatchlist`, `useRemoveFromWatchlist`)
- [ ] Add "Watch" / "Unwatch" button to package cards
- [ ] Add watchlist toggle to filter releases by watched packages only
- [ ] Show watchlist status on package detail page
- [ ] Update Home page to show user's watched packages vs all packages

---

## Epic: Home Page Redesign

> **Status: Not Yet Implemented**

Rebuild the home page to display grouped release summaries instead of individual releases.

**Preview:** `HomePageV2.tsx` with mock data available at `/preview` (branch: `feature/homepage-v2`)

### Feature: Summary Display

- [ ] Replace individual release cards with summary cards
- [ ] Show summary per package/major version group
- [ ] Display package name, version range (e.g., "v16.x"), and AI summary
- [ ] Indicate stable vs prerelease summaries visually
- [ ] Show release count per summary (e.g., "3 releases")
- [ ] Add "last updated" timestamp

### Feature: Expand/Collapse

- [ ] Allow expanding a summary to see individual releases
- [ ] Lazy load release details on expand
- [ ] Link to full release notes on GitHub

### Feature: Layout

- [ ] Group summaries by package or by date (user preference)
- [ ] Add tabs or toggle: "All Packages" vs "My Watchlist"
- [ ] Mobile-responsive summary cards
- [ ] Loading skeletons for summary cards

### Feature: Navigation

- [ ] Click summary → package detail page with all version summaries
- [ ] Click individual release → release detail page
- [ ] Update package detail page to show summaries

---

## Epic: Search, Sort & Filter

> **Status: Not Yet Implemented**

Server-side search with sorting and filtering options for releases.

### Feature: Backend

- [ ] Add `GET /api/search?q=...` endpoint
- [ ] Implement package name search
- [ ] Implement release title/body full-text search
- [ ] Add sort parameter (`publishedAt`, `packageName`)
- [ ] Add sort direction parameter (`asc`, `desc`)

### Feature: Frontend

- [ ] Wire up search input in `Home.tsx`
- [ ] Add debounced search with loading state
- [ ] Display search results with package/release grouping
- [ ] Add prerelease filter toggle to UI
- [ ] Add major version filter dropdown
- [ ] Add date range picker for releases
- [ ] Add sort controls (newest first, oldest first, by package)
- [ ] Persist filter/sort preferences to user settings

---

## Epic: Email Notifications

> **Status: Not Yet Implemented**

Send transactional emails using Azure Functions (Node.js/TypeScript), Resend, and React Email templates.

### Feature: Infrastructure

- [ ] Create new Azure Functions project (Node.js v4 model, TypeScript)
- [ ] Add Resend SDK package
- [ ] Add React Email packages (`@react-email/components`, `react-email`)
- [ ] Configure Resend API key in Azure Functions settings
- [ ] Set up deployment pipeline for Azure Functions

### Feature: Email Templates

- [ ] Set up React Email project structure
- [ ] Create base email layout template
- [ ] Create "Welcome" email template (new user signup)
- [ ] Create "New Release" email template (watched package has new release)
- [ ] Create "Weekly Digest" email template (summary of releases)
- [ ] Create "Subscription Confirmed" email template
- [ ] Add email preview/dev server for template development

### Feature: Azure Functions

- [ ] Create HTTP trigger function for sending emails
- [ ] Create queue trigger function for batch email processing
- [ ] Create timer trigger function for weekly digest emails
- [ ] Implement email sending via Resend API
- [ ] Add logging and error handling

### Feature: Integration

- [ ] Add email preferences to user settings (opt-in/out per email type)
- [ ] Trigger welcome email on user signup (from API or Stytch webhook)
- [ ] Trigger release notification emails from sync service
- [ ] Queue digest emails for subscribed users

---

## Epic: Subscriptions

> **Status: Not Yet Implemented**

Allow users to purchase Pro or Max subscriptions via Stripe hosted checkout. Subscription tiers and pricing managed in Stripe.

### Feature: Backend

- [ ] Add Stripe SDK package
- [ ] Add Stripe configuration (API key, webhook secret)
- [ ] Add `subscriptionTier` and `stripeCustomerId` fields to `User` entity
- [ ] Add migration for subscription fields
- [ ] Add `POST /api/subscriptions/checkout` endpoint (create Stripe Checkout session)
- [ ] Add `GET /api/subscriptions/portal` endpoint (create Stripe Customer Portal session)
- [ ] Add `GET /api/subscriptions/status` endpoint (get current subscription status)
- [ ] Add `POST /api/webhooks/stripe` endpoint for Stripe webhooks
- [ ] Handle `checkout.session.completed` webhook event
- [ ] Handle `customer.subscription.updated` webhook event
- [ ] Handle `customer.subscription.deleted` webhook event
- [ ] Verify Stripe webhook signatures

### Feature: Frontend

- [ ] Add subscription status to user context
- [ ] Create pricing page with Pro/Max tiers
- [ ] Add "Upgrade" button that redirects to Stripe Checkout
- [ ] Add "Manage Subscription" button that redirects to Stripe Customer Portal
- [ ] Show current subscription tier in user menu/settings
- [ ] Handle return from Stripe Checkout (success/cancel URLs)

### Feature: Entitlements

- [ ] Define feature limits per tier (Free vs Pro vs Max)
- [ ] Add middleware/helper to check subscription tier
- [ ] Gate premium features based on subscription status

---

## Bugs

- [ ] **Duplicate type definition** - `AddPackageRequest` defined twice in `types.ts`
- [ ] **API auth inconsistency** - `/api/releases/{id}/summarize` has auth but README says "No"
- [ ] **Wrong API Endpoint** - The API endpoint is wrong in the Readme, and should be a GH variable
- [ ] **Finish setting up Stytch** - Configure Stytch keys in both FE and backend, and setup project in Stytch portal

---

## Data

- [ ] **Figure out default packages** - Figure out if we want the top 10 packages from NPM or top 10 projects in GitHub

---

## Infrastructure

- [ ] **Set up Dependabot** - Configure automated dependency updates for npm and NuGet packages
