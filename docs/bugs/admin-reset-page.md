# Admin Reset Page Issues

## "Reset" button on Health page doesn't reset releases

**Page:** `/admin/health`

The "Reset" button on the health page calls `useResetPackageSync` which hits `POST /api/admin/packages/{id}/reset-sync`. This only resets failure tracking (`ConsecutiveFailures`, `IsSyncDisabled`, `LastFailureMessage`) — it does **not** delete releases or summaries.

To actually reset releases, you must navigate to `/admin/reset` and use the "Reset Releases" button there, which calls `POST /api/admin/packages/{id}/reset-releases` and deletes all releases/summaries + clears `LastFetchedAt`.

### Suggested fix

Either:

- Add a "Reset Releases" button to the health page (using `useResetReleases` from hooks)
- Or add a link from the health page to `/admin/reset`
- Rename the health page "Reset" button to "Re-enable Sync" to clarify its purpose

## Pagination shows literal `\u2013` instead of en dash

**Page:** `/admin/reset`

The pagination text "Showing 1\u201320 of 233" renders the unicode escape literally because `\u2013` is in a JSX text node, not inside a JS string. Fixed by wrapping it as `{'\u2013'}`.
