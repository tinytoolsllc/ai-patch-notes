Query App Insights for the PatchNotes applications and report a health summary.

Use `az monitor log-analytics query` against the Log Analytics workspace `db615771-fd41-42d6-9b98-4161ee58f879` (workspace-based App Insights).

The optional argument `$ARGUMENTS` specifies the time range (e.g. "1h", "6h", "24h", "7d"). Default to "24h" if not provided.

Run the following queries in parallel, then summarize the results concisely:

## Queries to run

**1. Exceptions across all apps**
```
az monitor log-analytics query -w db615771-fd41-42d6-9b98-4161ee58f879 --analytics-query "AppExceptions | where TimeGenerated > ago({{timeRange}}) | summarize count() by AppRoleName, ExceptionType=tostring(Properties.type), OuterMessage | order by count_ desc | take 20"
```

**2. Failed requests**
```
az monitor log-analytics query -w db615771-fd41-42d6-9b98-4161ee58f879 --analytics-query "AppRequests | where TimeGenerated > ago({{timeRange}}) and Success == false | summarize count() by AppRoleName, OperationName, ResultCode | order by count_ desc | take 20"
```

**3. Sync function invocations & custom events**
```
az monitor log-analytics query -w db615771-fd41-42d6-9b98-4161ee58f879 --analytics-query "AppRequests | where TimeGenerated > ago({{timeRange}}) | where AppRoleName =~ 'fn-patchnotes-sync' | where OperationName =~ 'SyncReleases' | summarize invocations=count(), successes=countif(Success == true), failures=countif(Success == false), avgDuration=avg(DurationMs) | project invocations, successes, failures, avgDurationSec=round(avgDuration/1000, 1)"
```

```
az monitor log-analytics query -w db615771-fd41-42d6-9b98-4161ee58f879 --analytics-query "AppEvents | where TimeGenerated > ago({{timeRange}}) and Name in ('SyncFunctionStarted', 'SyncReleasesCompleted', 'SyncReleasesFailed') | project TimeGenerated, Name, Properties | order by TimeGenerated desc | take 10"
```

**4. Email function invocations & custom events**
```
az monitor log-analytics query -w db615771-fd41-42d6-9b98-4161ee58f879 --analytics-query "AppRequests | where TimeGenerated > ago({{timeRange}}) | where AppRoleName =~ 'fn-patchnotes-email' | where OperationName =~ 'sendDigest' | summarize invocations=count(), successes=countif(Success == true), failures=countif(Success == false), avgDuration=avg(DurationMs) | project invocations, successes, failures, avgDurationMs=round(avgDuration, 0)"
```

```
az monitor log-analytics query -w db615771-fd41-42d6-9b98-4161ee58f879 --analytics-query "AppEvents | where TimeGenerated > ago({{timeRange}}) and Name in ('EmailFunctionStarted', 'DigestCompleted', 'WelcomeEmailSent', 'WelcomeEmailFailed') | project TimeGenerated, Name, Properties | order by TimeGenerated desc | take 10"
```

**5. Error traces**
```
az monitor log-analytics query -w db615771-fd41-42d6-9b98-4161ee58f879 --analytics-query "AppTraces | where TimeGenerated > ago({{timeRange}}) and SeverityLevel >= 3 | summarize count() by AppRoleName, Message | order by count_ desc | take 20"
```

## Output format

After running all queries, report a concise health summary:

- **Overall status**: Healthy / Warning / Unhealthy
- **API**: exception count, failed request count
- **Sync Function**: invocation count, success/failure, last run outcome (from SyncReleasesCompleted/Failed events), any errors
- **Email Function**: invocation count, success/failure, recent digest outcomes, welcome email success/failure counts
- **Notable errors**: List any exceptions or error traces with counts

If any query fails (e.g. az CLI not logged in), report the error and continue with remaining queries.
