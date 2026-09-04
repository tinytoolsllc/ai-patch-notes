# Landing Page Improvement Plan

> Audit date: 2026-02-13
> Site: https://www.myreleasenotes.ai/
> Tech stack: React 19, TanStack Router, Tailwind CSS 4, Vite 7, ASP.NET Core API

---

## Current State Assessment

### What Works

- Clean card-based release feed with good visual hierarchy
- Dark mode toggle is smooth and well-implemented
- Mobile layout is responsive and usable
- Grouping, sorting, and pre-release filtering all function correctly
- Pricing page exists with clear Free vs Pro tiers
- Stytch-based auth is simple (email magic link)

### Critical Problems

| Issue                                                                    | Severity | File(s)                         |
| ------------------------------------------------------------------------ | -------- | ------------------------------- |
| No hero/marketing landing page - visitors land on a raw data feed        | Critical | `HomePage.tsx`                  |
| All SEO meta tags missing (description, og:\*, twitter:\*, canonical)    | Critical | `index.html`                    |
| Brand mismatch - domain is `myreleasenotes.ai`, title says "Patch Notes" | High     | `index.html`, `HeaderTitle.tsx` |
| Favicon is default `vite.svg`                                            | High     | `index.html`                    |
| No value proposition visible to first-time visitors                      | Critical | `HomePage.tsx`                  |
| Icon-only toolbar with no tooltips or labels                             | Medium   | `HomePage.tsx`                  |
| Raw markdown dump in expanded releases - URLs not clickable              | High     | Release components              |
| Footer is just "Forged in Gas Town" - no links or navigation             | Medium   | `Footer.tsx`                    |
| No social proof, testimonials, or trust signals                          | Medium   | N/A                             |
| No search/filter for finding packages                                    | Medium   | `HomePage.tsx`                  |

---

## Phase 1: Hero Section & First Impressions ✅ DONE

**Goal:** Give first-time visitors an immediate understanding of what Patch Notes does and why they should sign up.

**Implementation:** All four sub-items (hero, features, how-it-works, pricing) are delivered as a single inline **HeroCard carousel** component rather than separate full-page sections. The carousel sits above the release feed on the home page for logged-out users only.

### Approach: Inline HeroCard Carousel

File: `src/components/landing/HeroCard.tsx`

A single `Card`-based component with 4 carousel slides controlled by `useState`:

1. **Hero** — Logo, headline ("Never miss a release that matters"), subheadline, "Get Started Free" CTA → `/login`
2. **Features** — 4 features in a compact 2×2 grid (icons + short text): AI Summaries, Track Any Package, Smart Filtering, Works Everywhere
3. **How It Works** — 3 numbered steps: Sign up, Pick packages, Stay updated
4. **Pricing** — Free vs Pro side-by-side with feature checklists

**Navigation:** Dot indicators at bottom + prev/next chevron buttons on the sides.

**Dismiss:** X button in top-right corner. Persists via `useFilterStore` (zustand + persist, uses existing `localStorage` key `patchnotes-filters`).

```
┌──────────────────────────────────────┐
│  [←]                        [✕]      │
│                                      │
│         Slide Content Here           │
│                                      │
│                              [→]     │
│                                      │
│           ● ● ● ●  (dots)           │
└──────────────────────────────────────┘
```

**Key decisions:**

- Replaced the previous separate `/preview` landing page approach — all content is now inline on the home page
- No external carousel library — just index state + conditional rendering
- Uses existing `Card`, `Button`, `Logo` components and Lucide icons
- Responsive: single-column grids on mobile for features/pricing slides
- Dark mode compatible via existing design token classes

> **Previous approach (removed):** A standalone `/preview` route with separate `HeroSection.tsx`, `FeaturesSection.tsx`, `HowItWorksSection.tsx`, and `PricingPreview.tsx` files was created and then deleted in favor of this inline approach.

---

## Phase 2: SEO & Meta Tags

**Goal:** Make the site discoverable and shareable.

### 2.1 Add meta tags to `index.html` ✅ DONE

```html
<title>Patch Notes - Track GitHub Releases | myreleasenotes.ai</title>
<meta
  name="description"
  content="Track GitHub releases for the packages you depend on. AI-powered summaries, smart filtering, dark mode, and instant notifications. Free to start."
/>

<!-- Open Graph -->
<meta property="og:type" content="website" />
<meta property="og:url" content="https://www.myreleasenotes.ai/" />
<meta property="og:title" content="Patch Notes - Track GitHub Releases" />
<meta
  property="og:description"
  content="Never miss a release that matters. AI-powered summaries, smart filtering, and notifications for your favorite packages."
/>
<meta property="og:image" content="https://www.myreleasenotes.ai/og-image.png" />

<!-- Twitter Card -->
<meta name="twitter:card" content="summary_large_image" />
<meta name="twitter:title" content="Patch Notes - Track GitHub Releases" />
<meta
  name="twitter:description"
  content="Never miss a release that matters. AI-powered summaries, smart filtering, and notifications for your favorite packages."
/>
<meta name="twitter:image" content="https://www.myreleasenotes.ai/og-image.png" />

<!-- Canonical -->
<link rel="canonical" href="https://www.myreleasenotes.ai/" />
```

### 2.2 Replace the favicon ✅ DONE

- Replaced `vite.svg` with `favicon.svg` — the same notepad+box logo used in the header
- Updated `index.html` to reference `favicon.svg`
- Deleted the default `vite.svg`
- Still TODO: Add `apple-touch-icon` (180x180 PNG), `manifest.json` with icon sizes

### 2.3 Add structured data (JSON-LD) ✅ DONE

Added to `index.html`:

```html
<script type="application/ld+json">
  {
    "@context": "https://schema.org",
    "@type": "WebApplication",
    "name": "Patch Notes",
    "url": "https://www.myreleasenotes.ai",
    "description": "Track GitHub releases for the packages you depend on.",
    "applicationCategory": "DeveloperApplication",
    "operatingSystem": "Web",
    "offers": {
      "@type": "Offer",
      "price": "0",
      "priceCurrency": "USD"
    }
  }
</script>
```

### 2.4 Create an OG image

- Design a 1200x630 branded image for social sharing
- Include: logo, tagline, product screenshot
- Save as `public/og-image.png`
- Consider dynamic OG images per package/release in a future phase

---

## Phase 3: UX Polish

**Goal:** Improve clarity, content readability, and navigation.

### 3.1 Fix the toolbar UX ✅ DONE

Current state: 4 icon-only buttons with no labels or tooltips.

**Option A (Recommended): Add tooltip on hover**

- Wrap each button in a tooltip component
- Tooltip text: "Hide pre-releases", "Group by package", "Sort by name", "Sort by date"
- Use a lightweight tooltip (CSS-only or a small component)

**Option B: Add text labels on desktop**

- Show icon + text on screens >= 768px
- Icon-only on mobile (with tooltips)

**Also fix:** Active/toggled state needs stronger visual distinction. Currently the icon changes subtly (e.g., strikethrough on the pre-release icon). Consider:

- Background color change on active buttons (e.g., `bg-primary-100 dark:bg-primary-900`)
- Or a visible pressed/selected border

### 3.2 Fix release note content rendering

Current state: Expanded release notes show raw markdown text with full GitHub URLs displayed inline as plain text. This is the most jarring UX issue.

**Fix:**

- The project already includes `react-markdown` - ensure it's used for release body rendering
- Parse and render:
  - Markdown links as clickable `<a>` tags with `target="_blank"`
  - PR references (`#1234`) as links to the GitHub PR
  - `@username` mentions as links to GitHub profiles
  - Code blocks with proper syntax highlighting
  - Headers (`###`) with appropriate sizing
- Truncate long release notes with "Show more" expansion
- Add `prose` Tailwind typography classes for readable text formatting

**Example before/after:**

```
BEFORE:
### Breaking Changes * — Drop support for Python 3.9. PR [#14897](https://github.com/fastapi/fastapi/pull/14897) by [@tiangolo](https://github.com/tiangolo).

AFTER:
### Breaking Changes
* Drop support for Python 3.9. PR #14897 by @tiangolo
(with #14897 and @tiangolo as clickable links)
```

### 3.3 Improve the footer ✅ DONE

Full footer redesign completed:

- Left: "**My Release Notes** by [Tiny Tools](https://www.yourtinytools.com)" (inline)
- Right: Navigation links (Home, Pricing, About, Privacy)
- Bottom: "Forged in Gas Town · © 2026 My Release Notes · A Tiny Tools product" (single line)
- Below: GitHub trademark disclaimer (left-aligned)
- Responsive: stacks vertically on mobile
- Compact spacing, non-sticky header

New files created:

- `src/pages/About.tsx` — About page with brand info, Gas Town story, Tiny Tools attribution
- `src/routes/about.tsx` — TanStack Router file route for `/about`

Additional changes:

- Header changed from sticky to relative (scrolls with content)
- "by Tiny Tools" subtitle added under header title on home page
- Pricing card buttons pinned to bottom with `flex-col` + `mt-auto`

### 3.4 Add search/filter functionality

Current state: No way for users to search for or filter packages in the feed.

**Implementation:**

- Add a search input above the feed (below the toolbar)
- Filter release cards client-side by package name
- Debounced input (300ms) for performance
- Placeholder: "Filter packages..."
- Clear button when text is entered
- Use existing `Input` component from `src/components/ui/Input.tsx`

---

## Phase 4: Brand Consistency

**Goal:** Resolve naming confusion and establish visual identity.

### 4.1 Resolve brand naming ✅ DONE

Rebranded from "Patch Notes" → "My Release Notes" with "by Tiny Tools" attribution:

- `index.html`: title, og:title, twitter:title, JSON-LD name
- `HomePage.tsx`: HeaderTitle + "by Tiny Tools" subtitle
- `subscription-canceled.tsx`: HeaderTitle
- `subscription-success.tsx`: HeaderTitle + "Welcome to My Release Notes Pro!"
- `Privacy.tsx`: Entity name → "My Release Notes ... operated by Tiny Tools LLC"
- Footer: brand name + Tiny Tools links throughout
- Internal identifiers (localStorage keys, package name, API codegen) left unchanged

### 4.2 Design a logo

Current state: Plain text "Patch Notes" in the header.

**Requirements:**

- Logo mark (icon) + wordmark (text)
- Works in both light and dark mode
- Scales well for favicon (16x16), header (32px height), and OG image
- Suggested concepts:
  - A tag/label icon (like a Git tag) with a checkmark
  - A package icon with a notification dot
  - An arrow-up icon (representing updates/releases)
- Use the existing brand blue (`primary-600`) as the primary logo color

---

## Phase 5: Conversion & Trust

**Goal:** Increase sign-up rates and build credibility.

### 5.1 Add social proof

Options (pick what's available):

- "Tracking X releases across Y packages" - live counter from the API
- GitHub stars badge (if repo is public)
- Brief testimonial quotes from users
- "Used by developers at [company logos]" (if applicable)

**Implementation:**

- Add a stats bar below the hero section
- Query the API for total release/package counts
- Display as: "Tracking 5,000+ releases across 200+ packages"

### 5.2 Improve the sign-in banner ✅ DONE

The plain "Sign in to customize your feed" banner has been replaced by the inline HeroCard carousel (see Phase 1). The carousel is dismissible with state persisted to localStorage via zustand.

### 5.3 Add email capture for notifications

For users who want updates without a full account:

- Small form: "Get a weekly digest of releases" + email input + submit
- Place below the feed or in the footer
- Aligns with the Pro "email highlights" feature as an upsell path

---

## Phase 6: Performance & Technical

**Goal:** Polish the experience with loading states, animations, and sharing.

### 6.1 Add loading skeletons

Current state: No visible loading states detected.

- Add skeleton card components that match the shape of `ReleaseCard`
- Show 3-4 skeleton cards while the feed loads
- Use Tailwind `animate-pulse` on gray placeholder blocks
- Create `src/components/ui/Skeleton.tsx` as a reusable primitive

### 6.2 Add page entrance animations

- Stagger card entrance animations (fade-in + slide-up, 50ms delay between cards)
- Hero section: fade-in on load
- Features section: fade-in on scroll (Intersection Observer)
- Keep animations subtle and fast (200-300ms duration)
- Respect `prefers-reduced-motion` media query

### 6.3 Dynamic OG images per release

Future enhancement for social sharing:

- Generate OG images dynamically for `/release/:id` and `/package/:id` routes
- Include: package name, version, release date, summary snippet
- Use an edge function or server-side rendering
- This makes shared release links look great on Twitter/Slack/Discord

### 6.4 Add a PWA manifest

The app already has `apple-mobile-web-app-capable` meta tags. Complete the PWA setup:

- Create `manifest.json` with app name, icons, theme colors
- Add `<link rel="manifest" href="/manifest.json">`
- Define `start_url`, `display: standalone`, `theme_color`

---

## Implementation Priority

| Priority | Item                                | Effort | Impact    | Phase |
| -------- | ----------------------------------- | ------ | --------- | ----- |
| ✅       | Add hero section for new visitors   | Medium | Very High | 1     |
| ✅       | Add features section                | Medium | High      | 1     |
| ✅       | Add "How It Works" section          | Low    | Medium    | 1     |
| ✅       | Add inline pricing preview          | Medium | Medium    | 1     |
| ✅       | Improve sign-in banner copy         | Low    | Medium    | 5.2   |
| ✅       | Add SEO meta tags to `index.html`   | Low    | Very High | 2.1   |
| ✅       | Replace vite.svg favicon            | Low    | High      | 2.2   |
| P1       | Fix release note markdown rendering | Low    | High      | 3.2   |
| ✅       | Add toolbar tooltips                | Low    | Medium    | 3.1   |
| ✅       | Resolve brand naming consistency    | Low    | Medium    | 4.1   |
| ✅       | Improve footer with navigation      | Low    | Medium    | 3.3   |
| P2       | Add search/filter for packages      | Medium | High      | 3.4   |
| P2       | Add social proof / stats            | Low    | Medium    | 5.1   |
| ✅       | Add structured data (JSON-LD)       | Low    | Medium    | 2.3   |
| P3       | Design a proper logo                | Medium | Medium    | 4.2   |
| P3       | Add loading skeletons               | Low    | Low       | 6.1   |
| P3       | Create OG image for sharing         | Medium | Medium    | 2.4   |
| P3       | Add page entrance animations        | Low    | Low       | 6.2   |
| P3       | Dynamic OG images per release       | High   | Medium    | 6.3   |
| P3       | Email capture / digest signup       | Medium | Medium    | 5.3   |
| P3       | Complete PWA manifest               | Low    | Low       | 6.4   |

---

## Files Changed (Phases 1, 2.1–2.3, partial 3.3 — complete)

| File                                                          | Status      | Changes                                                                                           |
| ------------------------------------------------------------- | ----------- | ------------------------------------------------------------------------------------------------- |
| `patchnotes-web/src/components/landing/HeroCard.tsx`          | ✅ Created  | Inline carousel with 4 slides; separate mobile/web components per slide; pricing comparison table |
| `patchnotes-web/src/components/landing/Logo.tsx`              | ✅ Created  | SVG logo component used in header title bar                                                       |
| `patchnotes-web/src/stores/filterStore.ts`                    | ✅ Modified | Added `heroDismissed` boolean + `dismissHero()` action                                            |
| `patchnotes-web/src/pages/HomePage.tsx`                       | ✅ Modified | HeroCard above filters, logo in header, filters below hero                                        |
| `patchnotes-web/src/pages/HomePage.test.tsx`                  | ✅ Modified | Updated tests for hero card instead of sign-in banner                                             |
| `patchnotes-web/index.html`                                   | ✅ Modified | SEO meta tags (og:_, twitter:_, canonical), JSON-LD structured data, favicon                      |
| `patchnotes-web/public/favicon.svg`                           | ✅ Created  | Branded notepad+box logo matching header                                                          |
| `patchnotes-web/public/vite.svg`                              | ❌ Deleted  | Default Vite favicon removed                                                                      |
| `patchnotes-web/src/components/ui/Footer.tsx`                 | ✅ Modified | Full redesign: brand + nav links, tagline + copyright, trademark disclaimer                       |
| `patchnotes-web/src/components/ui/Header.tsx`                 | ✅ Modified | Changed from sticky to relative positioning                                                       |
| `patchnotes-web/src/pages/About.tsx`                          | ✅ Created  | About page with brand info, Gas Town link, Tiny Tools attribution                                 |
| `patchnotes-web/src/routes/about.tsx`                         | ✅ Created  | TanStack Router file route for /about                                                             |
| `patchnotes-web/src/pages/Pricing.tsx`                        | ✅ Modified | Buttons pinned to bottom of cards with flex-col + mt-auto                                         |
| `patchnotes-web/src/pages/Privacy.tsx`                        | ✅ Modified | Entity → "My Release Notes ... operated by Tiny Tools LLC"                                        |
| `patchnotes-web/src/routes/subscription-canceled.tsx`         | ✅ Modified | HeaderTitle → "My Release Notes"                                                                  |
| `patchnotes-web/src/routes/subscription-success.tsx`          | ✅ Modified | HeaderTitle + "Welcome to My Release Notes Pro!"                                                  |
| `patchnotes-web/src/index.css`                                | ✅ Modified | Added `--font-size-2xs: 0.625rem` to `@theme` for `text-2xs` utility                              |
| `patchnotes-web/src/components/landing/HeroSection.tsx`       | ❌ Deleted  | Replaced by HeroCard carousel                                                                     |
| `patchnotes-web/src/components/landing/FeaturesSection.tsx`   | ❌ Deleted  | Replaced by HeroCard carousel                                                                     |
| `patchnotes-web/src/components/landing/HowItWorksSection.tsx` | ❌ Deleted  | Replaced by HeroCard carousel                                                                     |
| `patchnotes-web/src/components/landing/PricingPreview.tsx`    | ❌ Deleted  | Replaced by HeroCard carousel                                                                     |
| `patchnotes-web/src/pages/PreviewPage.tsx`                    | ❌ Deleted  | Standalone /preview page removed                                                                  |
| `patchnotes-web/src/routes/preview.tsx`                       | ❌ Deleted  | Route removed                                                                                     |

## Remaining Files to Create/Modify (future phases)

| File                                                    | Purpose                         | Phase      |
| ------------------------------------------------------- | ------------------------------- | ---------- |
| ~~`patchnotes-web/src/components/ui/Footer.tsx`~~       | ~~Navigation links, copyright~~ | ~~3.3~~ ✅ |
| `patchnotes-web/src/components/landing/SocialProof.tsx` | Stats bar / testimonials        | 5.1        |
| `patchnotes-web/src/components/ui/Tooltip.tsx`          | Reusable tooltip component      | 3.1        |
| `patchnotes-web/src/components/ui/Skeleton.tsx`         | Loading skeleton primitive      | 6.1        |
| `patchnotes-web/public/og-image.png`                    | Social sharing image            | 2.4        |
| `patchnotes-web/public/manifest.json`                   | PWA manifest                    | 6.4        |
