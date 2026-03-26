# design-review: Visual Design Audit & Fix

Designer's eye QA: finds visual inconsistency, spacing issues, hierarchy problems, AI slop
patterns, and slow interactions — then fixes them in source code. Use when asked to "audit
the design", "visual QA", "check if it looks good", or "design polish".

## How It Works

Uses `mcp__chrome-devtools__*` tools to evaluate the rendered site visually, then edits source
code to fix issues found. Always evaluate what's rendered, not just what's in the code.

## Setup

1. Determine target URL (ask if not provided; if on a feature branch with no URL, detect local dev server)
2. Check for `DESIGN.md` or `design-system.md` in the repo — calibrate against it if found
3. Ensure clean working tree (`git status`) — each fix gets its own atomic commit

## Modes

| Mode | Pages | Use when |
|------|-------|----------|
| Quick (`--quick`) | Homepage + 2 | Fast design check |
| Standard (default) | 5-8 pages | Normal audit |
| Deep (`--deep`) | 10-15 pages | Pre-launch polish |
| Diff-aware | Changed pages only | On a feature branch |

## Phase 1: First Impression

Form a gut reaction before analyzing anything:

1. `navigate_page` to target URL
2. `take_screenshot` — full page
3. Write a structured first impression:
   - "The site communicates **[what]**."
   - "I notice **[observation]**."
   - "The first 3 things my eye goes to are: **[1]**, **[2]**, **[3]**."
   - "If I had to describe this in one word: **[word]**."

## Phase 2: Design System Extraction

Extract the actual design system from the rendered page using `evaluate_script`:

```js
// Fonts in use
() => JSON.stringify([...new Set([...document.querySelectorAll('*')].slice(0,500).map(e => getComputedStyle(e).fontFamily))])

// Color palette
() => JSON.stringify([...new Set([...document.querySelectorAll('*')].slice(0,500).flatMap(e => [getComputedStyle(e).color, getComputedStyle(e).backgroundColor]).filter(c => c !== 'rgba(0, 0, 0, 0)'))])

// Heading hierarchy
() => JSON.stringify([...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].map(h => ({tag:h.tagName, text:h.textContent.trim().slice(0,50), size:getComputedStyle(h).fontSize, weight:getComputedStyle(h).fontWeight})))

// Touch target audit
() => JSON.stringify([...document.querySelectorAll('a,button,input,[role=button]')].filter(e => {const r=e.getBoundingClientRect(); return r.width>0 && (r.width<44||r.height<44)}).map(e => ({tag:e.tagName, text:(e.textContent||'').trim().slice(0,30), w:Math.round(e.getBoundingClientRect().width), h:Math.round(e.getBoundingClientRect().height)})).slice(0,20))
```

## Phase 3: Page-by-Page Audit

For each page, navigate and evaluate against this checklist:

### Visual Hierarchy & Composition
- Clear focal point? One primary CTA per view?
- Information density appropriate? White space intentional?
- Above-the-fold communicates purpose in 3 seconds?

### Typography
- Font count <= 3?
- Line-height: 1.5x body, 1.15-1.25x headings?
- Measure: 45-75 chars per line?
- Heading hierarchy sequential (no skipped levels)?
- Body text >= 16px?

### Color & Contrast
- WCAG AA: body text 4.5:1, large text 3:1, UI components 3:1?
- Semantic colors consistent (success=green, error=red)?
- No color-only encoding?
- Dark mode: surfaces use elevation, text off-white (~#E0E0E0)?

### Spacing & Layout
- Spacing uses a scale (4px or 8px base)?
- Grid consistent at all breakpoints?
- No horizontal scroll on mobile?
- Max content width set?

### Interaction States
- Hover state on all interactive elements?
- `focus-visible` ring present?
- Disabled state: reduced opacity + `cursor: not-allowed`?
- Loading states use skeletons matching content shape?
- Empty states: warm message + action?
- Touch targets >= 44px?

### Responsive Design
Test with `emulate`:
- Mobile: `375x812,mobile,touch`
- Tablet: `768x1024,touch`
- Desktop: `1280x720`

Evaluate:
- Mobile layout makes design sense (not just stacked columns)?
- Navigation collapses appropriately?
- No horizontal scroll at any viewport?

### AI Slop Detection (the blacklist)
Flag if ANY of these patterns appear:
- Purple/violet gradient backgrounds
- 3-column feature grid with icon-in-circle + title + description
- Icons in colored circles as section decoration
- Centered everything (all headings, descriptions, cards)
- Uniform bubbly border-radius on every element
- Decorative blobs, floating circles, wavy SVG dividers
- Emoji as design elements
- Colored left-border on cards
- Generic hero copy ("Welcome to [X]", "Unlock the power of...")

### Performance as Design
- Run `lighthouse_audit` for scores
- Run `performance_start_trace` for Core Web Vitals
- LCP < 2.0s? CLS < 0.1?
- Images lazy-loaded with dimensions set?

## Phase 4: Interaction Flow Review

Walk 2-3 key user flows:
1. `take_snapshot` to find interactive elements
2. `click` / `fill` / interact
3. `take_snapshot` again to see what changed
4. Evaluate: response feel, transition quality, feedback clarity

## Phase 5: Fix Issues

For each finding:
1. Locate the source code responsible
2. Fix the issue in code
3. Verify the fix in the browser (navigate, screenshot, compare)
4. Commit atomically: `git commit -m "design: fix [specific issue]"`

## Phase 6: Report

Write report to `docs/design-audit-{domain}-{date}.md` with:

### Scoring

**Design Score: {A-F}** — weighted average across categories:
| Category | Weight |
|----------|--------|
| Visual Hierarchy | 15% |
| Typography | 15% |
| Spacing & Layout | 15% |
| Color & Contrast | 10% |
| Interaction States | 10% |
| Responsive | 10% |
| Content Quality | 10% |
| AI Slop | 5% |
| Motion | 5% |
| Performance | 5% |

**AI Slop Score: {A-F}** — standalone grade

**Grades:** A (intentional, polished) → B (solid, minor issues) → C (functional, generic) → D (noticeable problems) → F (needs rework)

## Important Rules

1. **Think like a designer.** Care about whether things feel right, not just whether they work.
2. **Screenshot everything.** Use `take_screenshot` and Read the PNG to show the user.
3. **Be specific.** "Change X to Y because Z" — not "the spacing feels off."
4. **Fix iteratively.** One issue per commit. Verify each fix before moving on.
5. **AI Slop detection is key.** Be direct about generic AI-generated-looking patterns.
6. **Quick wins section.** Always include 3-5 highest-impact fixes that are easy to implement.
7. **Responsive is design.** Stacked desktop columns on mobile is not responsive design.
8. **Show screenshots.** After `take_screenshot`, use Read on the PNG so the user sees it.

## Design Critique Format

Use structured feedback:
- "I notice..." — observation
- "I wonder..." — question
- "What if..." — suggestion
- "I think... because..." — reasoned opinion
