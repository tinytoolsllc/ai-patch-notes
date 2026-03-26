# browse: Browser QA Testing & Dogfooding

Use Chrome DevTools MCP to navigate, interact with, and inspect web pages. Use when asked to
"open in browser", "test the site", "take a screenshot", "check the page", or "dogfood this".

## How It Works

This skill uses the `mcp__chrome-devtools__*` tools to control a real Chrome browser. Elements are
identified by `uid` values from page snapshots (accessibility tree). Always take a snapshot first
to discover element uids before interacting.

## Tool Reference

| Action | MCP Tool | Notes |
|--------|----------|-------|
| Navigate | `navigate_page` | `type: "url"`, or `"back"`, `"forward"`, `"reload"` |
| Snapshot | `take_snapshot` | A11y tree with uids — your primary way to see the page |
| Screenshot | `take_screenshot` | Visual capture; use `fullPage: true` for full page |
| Click | `click` | Pass `uid` from snapshot |
| Fill input | `fill` | Pass `uid` and `value` |
| Fill form | `fill_form` | Multiple fields at once: `elements: [{uid, value}, ...]` |
| Type text | `type_text` | Types into focused element; optional `submitKey` |
| Press key | `press_key` | `"Enter"`, `"Tab"`, `"Control+A"`, etc. |
| Hover | `hover` | Pass `uid` |
| Drag | `drag` | `from_uid` and `to_uid` |
| Upload file | `upload_file` | `uid` of file input + `filePath` |
| Run JS | `evaluate_script` | Pass a function string: `() => { return document.title }` |
| Console logs | `list_console_messages` | Filter by `types: ["error", "warn"]` for errors only |
| Console detail | `get_console_message` | Get full message by `msgid` |
| Network requests | `list_network_requests` | Filter by `resourceTypes` |
| Network detail | `get_network_request` | Get full request/response by `reqid` |
| Handle dialog | `handle_dialog` | `action: "accept"` or `"dismiss"`, optional `promptText` |
| Wait for text | `wait_for` | Waits for any of the provided texts to appear |
| Resize | `resize_page` | Set `width` and `height` |
| Emulate | `emulate` | Viewport, dark mode, network throttling, geolocation, user agent |
| List pages | `list_pages` | See all open tabs |
| Select page | `select_page` | Switch active tab by `pageId` |
| New page | `new_page` | Open URL in new tab |
| Close page | `close_page` | Close tab by `pageId` |
| Lighthouse | `lighthouse_audit` | Accessibility, SEO, best practices audit |
| Perf trace | `performance_start_trace` / `performance_stop_trace` | Core Web Vitals |
| Memory snapshot | `take_memory_snapshot` | Heap snapshot for leak debugging |

## Core QA Patterns

### 1. Verify a page loads correctly

```
navigate_page → url
take_snapshot                         # content loads?
list_console_messages → types: error  # JS errors?
list_network_requests                 # failed requests?
```

### 2. Test a user flow

```
navigate_page → login page
take_snapshot                         # see all interactive elements + uids
fill → uid of email field, value
fill → uid of password field, value
click → uid of submit button
wait_for → ["Dashboard", "Welcome"]   # wait for success state
take_snapshot                         # verify result
```

### 3. Verify an action worked

```
take_snapshot                         # baseline
click → uid of target element
take_snapshot                         # compare: what changed?
```

### 4. Visual evidence for bug reports

```
take_screenshot                       # viewport screenshot
take_screenshot → fullPage: true      # full page
list_console_messages → types: error  # error log
```

Always use the Read tool on screenshot PNGs so the user can see them.

### 5. Fill out a form efficiently

```
take_snapshot                         # find all form field uids
fill_form → elements: [              # fill multiple fields at once
  {uid: "...", value: "..."},
  {uid: "...", value: "..."}
]
click → uid of submit button
```

### 6. Test responsive layouts

```
# Test mobile
emulate → viewport: "375x812,mobile,touch"
take_screenshot → filePath: /tmp/mobile.png

# Test tablet
emulate → viewport: "768x1024,touch"
take_screenshot → filePath: /tmp/tablet.png

# Test desktop
emulate → viewport: "1280x720"
take_screenshot → filePath: /tmp/desktop.png
```

### 7. Run a Lighthouse audit

```
navigate_page → target URL
lighthouse_audit → device: "mobile"   # or "desktop"
```

### 8. Test dialogs

```
# Trigger the dialog via click, then:
handle_dialog → action: "accept"      # or "dismiss"
take_snapshot                          # verify result
```

### 9. Debug performance

```
navigate_page → target URL
performance_start_trace → reload: true
# trace auto-stops after page load
# review insights from the trace results
performance_analyze_insight → insightSetId, insightName
```

### 10. Compare pages side by side

```
navigate_page → staging URL
take_snapshot → filePath: /tmp/staging.txt
new_page → production URL
take_snapshot → filePath: /tmp/prod.txt
# Compare the two snapshots
```

### 11. Test with different conditions

```
# Dark mode
emulate → colorScheme: "dark"
take_screenshot

# Slow network
emulate → networkConditions: "Slow 3G"
navigate_page → reload
take_screenshot

# Geolocation
emulate → geolocation: "51.5074x-0.1278"  # London
```

## Important Notes

- **Snapshot first:** Always `take_snapshot` before interacting. You need uids to click/fill/hover.
- **Uids change on navigation:** After `navigate_page`, take a fresh snapshot.
- **Show screenshots:** After `take_screenshot`, use the Read tool on the PNG so the user sees it.
- **Use `includeSnapshot: true`** on click/fill/hover to get updated uids in the response without a separate snapshot call.
- **JS evaluation:** Pass a function string, not raw expressions. Use `() => { ... }` format.
- **Dialogs block:** If a dialog appears, handle it with `handle_dialog` before continuing.
- **Multiple tabs:** Use `list_pages` to see tabs, `select_page` to switch, `new_page` to open.
