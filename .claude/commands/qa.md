# qa: Report-Only QA Testing

Systematically QA test a web application and produce a structured report. This skill finds bugs
and documents them with screenshots and repro steps — it NEVER fixes anything.

Use when asked to "QA test", "find bugs", "test the site", or "smoke test". For fixing bugs, use a
separate workflow after reviewing this report.

## How It Works

Uses the `mcp__chrome-devtools__*` tools to control Chrome. Elements are identified by `uid` values
from page snapshots. Always take a snapshot first to discover uids before interacting.

## Modes

### Quick
Homepage + 2 key pages. 30-second smoke test. Use when you just want a fast health check.

### Standard (default)
5-8 pages. Full checklist evaluation, responsive screenshots, interaction flow testing.

### Deep
10-15 pages. Every interaction flow, exhaustive checklist. For pre-launch audits.

### Diff-aware
When on a feature branch with no URL specified, automatically scope testing to pages affected
by the branch changes. Analyze `git diff main...HEAD --name-only` to map changed files to routes.

## Workflow

### Phase 1: Initialize
1. Determine target URL (ask user if not provided)
2. Determine mode (quick/standard/deep)
3. Create output directory: `mkdir -p .qa-reports/screenshots`

### Phase 2: Authenticate (if needed)
1. Navigate to the target URL
2. Check if redirected to a login page
3. If auth required, ask the user how to proceed

### Phase 3: Orient
1. Take a full-page screenshot for baseline
2. Take a snapshot to map the page structure
3. Identify navigation elements and key pages to visit
4. Check console for errors: `list_console_messages` with `types: ["error", "warn"]`
5. Check network for failed requests: `list_network_requests`

### Phase 4: Explore & Test

For each page in scope:

**Page load checks:**
- `navigate_page` to the URL
- `take_snapshot` — does content render?
- `list_console_messages` with `types: ["error", "warn"]` — JS errors?
- `list_network_requests` — failed requests (4xx/5xx)?
- `take_screenshot` — visual evidence

**Responsive checks (standard/deep):**
- `emulate` viewport `375x812,mobile,touch` → `take_screenshot`
- `emulate` viewport `768x1024,touch` → `take_screenshot`
- `emulate` viewport `1280x720` → `take_screenshot`
- Check for horizontal scroll, overlapping elements, unreadable text

**Interaction checks (standard/deep):**
- `take_snapshot` to find interactive elements
- Test key user flows (forms, buttons, navigation)
- Verify actions produce expected results
- Check for missing loading states, error handling

**Accessibility spot checks:**
- Run `lighthouse_audit` with `device: "desktop"` and/or `"mobile"`
- Check for missing button labels, contrast issues, heading order

### Phase 5: Document Findings

For each bug found, document:
- **Title:** Brief description
- **Severity:** Critical / High / Medium / Low
- **Page:** URL where found
- **Steps to reproduce:** Numbered steps using the MCP tools
- **Expected:** What should happen
- **Actual:** What actually happens
- **Screenshot:** Reference to saved screenshot
- **Console errors:** Any related errors

### Phase 6: Compile Report

Write report to `.qa-reports/qa-report-{domain}-{YYYY-MM-DD}.md` with:

1. **Summary** — Overall health score and key metrics
2. **Health Score** — Weighted score across categories:
   - Console Errors (15%) — JS errors, unhandled exceptions
   - Broken Links/Requests (15%) — 4xx/5xx responses
   - Visual Issues (15%) — layout problems, responsive failures
   - Functional Issues (20%) — broken features, form failures
   - UX Issues (15%) — confusing flows, missing feedback
   - Performance (10%) — slow loads, large resources
   - Accessibility (10%) — Lighthouse a11y score, missing labels
3. **Findings** — Each bug with full details
4. **Pages Tested** — List of all URLs visited with pass/fail
5. **Screenshots Index** — Links to all captured screenshots

### Health Score Rubric

| Score | Label | Meaning |
|-------|-------|---------|
| 90-100 | Excellent | Production-ready, minor polish items only |
| 75-89 | Good | Solid, some issues to address before launch |
| 50-74 | Fair | Functional but needs attention |
| 25-49 | Poor | Significant issues affecting usability |
| 0-24 | Critical | Major blockers, not ready for users |

## Important Rules

1. **NEVER fix bugs.** Only find and document them. This is report-only.
2. **Screenshot everything.** Every finding needs visual evidence. Use `take_screenshot` and then Read the file to show the user.
3. **Reproduce before reporting.** Only report bugs you can demonstrate with the browser tools.
4. **Be specific.** "Button doesn't work" is not a finding. "Click on 'Submit' button (uid X) on /settings page results in console error: TypeError..." is.
5. **Test like a user.** Follow realistic user flows, not just check individual elements.
6. **Check console and network on every page.** Silent errors are still errors.
7. **Always test mobile.** Use `emulate` to check at least one mobile viewport.
8. **Use `includeSnapshot: true`** on interactions to efficiently track page changes.
9. **Run Lighthouse** at least once per session for accessibility/SEO/best-practices scores.
10. **Show screenshots to the user.** After `take_screenshot`, use the Read tool on the PNG so the user can see it.
