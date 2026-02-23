Query App Insights for the PatchNotes applications and report a health summary.

Use `az monitor app-insights query` against the `ai-patchnotes` App Insights resource in the `MyPkgUpdate` resource group.

The optional argument `$ARGUMENTS` specifies the time range (e.g. "1h", "6h", "24h", "7d"). Default to "24h" if not provided.

Run the following queries in parallel, then summarize the results concisely:

## Queries to run

**1. Exceptions across all apps**
```
az monitor app-insights query --app ai-patchnotes -g MyPkgUpdate --analytics-query "exceptions | where timestamp > ago({{timeRange}}) | summarize count() by cloud_RoleName, type, outerMessage | order by count_ desc | take 20"
```

**2. Failed requests**
```
az monitor app-insights query --app ai-patchnotes -g MyPkgUpdate --analytics-query "requests | where timestamp > ago({{timeRange}}) and success == false | summarize count() by cloud_RoleName, name, resultCode | order by count_ desc | take 20"
```

**3. Sync function custom events**
```
az monitor app-insights query --app ai-patchnotes -g MyPkgUpdate --analytics-query "customEvents | where timestamp > ago({{timeRange}}) and name in ('SyncFunctionStarted', 'SyncReleasesCompleted', 'SyncReleasesFailed') | project timestamp, name, customDimensions | order by timestamp desc | take 10"
```

**4. Email function custom events**
```
az monitor app-insights query --app ai-patchnotes -g MyPkgUpdate --analytics-query "customEvents | where timestamp > ago({{timeRange}}) and name in ('EmailFunctionStarted', 'DigestCompleted', 'WelcomeEmailSent', 'WelcomeEmailFailed') | project timestamp, name, customDimensions | order by timestamp desc | take 10"
```

**5. Error traces**
```
az monitor app-insights query --app ai-patchnotes -g MyPkgUpdate --analytics-query "traces | where timestamp > ago({{timeRange}}) and severityLevel >= 3 | summarize count() by cloud_RoleName, message | order by count_ desc | take 20"
```

## Output format

After running all queries, report a concise health summary:

- **Overall status**: Healthy / Warning / Unhealthy
- **API**: exception count, failed request count
- **Sync Function**: last run time, outcome (from SyncReleasesCompleted/Failed events), any errors
- **Email Function**: last cold start, recent digest outcomes, welcome email success/failure counts
- **Notable errors**: List any exceptions or error traces with counts

If any query fails (e.g. az CLI not logged in), report the error and continue with remaining queries.
