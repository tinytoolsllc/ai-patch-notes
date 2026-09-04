# Accessibility Audit — myreleasenotes.ai

**Date:** 2026-03-25
**Tool:** Lighthouse 13.0.3 (via Chrome DevTools MCP)
**URL:** https://www.myreleasenotes.ai/
**Score:** 85 / 100

## Failures

### 1. Buttons do not have an accessible name (Critical)

**WCAG:** 2.1 — 4.1.2 Name, Role, Value (Level A)
**Impact:** Critical — screen readers announce these as "button" with no context

**Failing elements:** 4 toolbar buttons on the home page (hide pre-releases, group by package, sort by name, sort by date)

```
Selector: div.flex > div.flex > div.relative > button.flex
```

Each button contains only an icon with no `aria-label`, inner text, or `title` attribute.

**Fix:** Add `aria-label` to each icon-only button describing its action (e.g., `aria-label="Hide pre-releases"`).

---

### 2. Insufficient color contrast (Serious)

**WCAG:** 2.1 — 1.4.3 Contrast Minimum (Level AA)
**Impact:** Serious — text may be unreadable for users with low vision

**Failing element:** "by Tiny Tools" subtitle in the header

```
Selector: div.flex > a.flex > div > p.text-2xs
Foreground: #6d7277
Background: #091018
Contrast ratio: 3.93:1 (required: 4.5:1 for text < 14pt)
```

**Fix:** Lighten the `text-text-tertiary` color to meet 4.5:1 contrast ratio, or increase font size to 14pt+ (where 3:1 is sufficient).

---

### 3. Heading order is not sequential (Moderate)

**WCAG:** Best practice
**Impact:** Moderate — assistive technology users may have difficulty navigating the page structure

**Failing element:** Release card "TL;DR" heading jumps to `<h4>` without a preceding `<h2>` or `<h3>`

```
Selector: div.bg-surface-primary > div.p-5 > div.text-sm > h4.text-xs
Node label: "TL;DR"
```

**Fix:** Ensure headings follow sequential order (h1 > h2 > h3 > h4). The hero uses `<h3>`, so release card subheadings should use `<h4>` only if there's an `<h3>` parent within the card — otherwise restructure the heading hierarchy.

---

### 4. Touch targets too small (Serious)

**WCAG:** 2.5.8 Target Size Minimum (Level AA)
**Impact:** Serious — users with motor impairments may have difficulty tapping these controls

**Failing elements:** Hero carousel dot indicators ("Go to slide 1", "Go to slide 2", etc.)

```
Selector: div.bg-surface-primary > div.flex > button.w-2
Size: 8px x 8px (required: 24px x 24px minimum)
Spacing: 4px between dots (required: 24px safe area)
```

**Fix:** Increase dot button size from `w-2 h-2` (8px) to at least `w-6 h-6` (24px), or add padding/invisible touch area to meet the 24px minimum. Increase gap between dots.

## Passing Audits

All other accessibility checks passed, including:

- `<html>` has valid `lang` attribute
- Images have `alt` text
- Links have descriptive text
- ARIA attributes are valid
- Page has landmark regions
- Form elements have labels
- No duplicate IDs
