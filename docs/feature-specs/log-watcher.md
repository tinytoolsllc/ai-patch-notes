# Log Watcher: Low-Cost Production Incident Triage for PatchNotes

> Hourly log review that groups recurring production failures, asks Ollama Cloud for severity and summary, and files sanitized GitHub issues.

## Motivation

The "bender" system in the current-ai project is a useful reference point: it ingests production alerts, analyzes them with an LLM, and files follow-up work automatically. PatchNotes needs a much smaller version of that idea:

- **Low operating cost**: reuse the existing Azure Functions app and Ollama Cloud setup
- **Low operational complexity**: no new workers, queues, MCP servers, or repo clones
- **Native workflow**: file directly to GitHub Issues instead of adding another tracker
- **Conservative behavior**: prioritize signal over coverage and avoid noisy auto-filed bugs

This does not need to be a near-real-time pager. PatchNotes code and traffic do not churn fast enough to justify a 15-minute loop. An **hourly run is sufficient to start**, and a slower cadence remains acceptable if noise stays low.

## Goals

- Detect recurring production failures from existing Azure telemetry
- Group them deterministically before involving an LLM
- Produce short, useful GitHub issues for new incidents
- Comment on recurrence without spamming existing issues
- Keep the design cheap, testable, and easy to disable

## Non-Goals

- Replacing alerting or on-call workflows
- Sending raw production logs to GitHub
- Letting the LLM invent dedupe keys or decide whether data is safe to share
- Monitoring every telemetry table from day one
- Creating issues for single transient blips

## Key Decisions

- **Cadence:** run hourly by default, not every 15 minutes
- **Scope:** start with API and sync-function telemetry only
- **Signal sources:** start with `exceptions` and failed `requests`; add `traces` only if needed
- **Dedupe:** fingerprint incidents in code, not via LLM output
- **Safety:** sanitize before sending data to Ollama and before filing GitHub issues
- **Observability:** use `ILogger` and the existing telemetry pipeline; do not add new `TelemetryClient` usage
- **Rollout:** dry-run first, then enable issue filing only for higher-severity incidents

## Architecture Overview

```
                   +------------------------+
                   | Azure Functions        |
                   | LogWatcher             |
                   | Hourly timer trigger   |
                   +-----------+------------+
                               |
                   1. query    | 2. sanitize + group
                               v
                   +------------------------+
                   | Application Insights / |
                   | Azure Monitor query    |
                   +-----------+------------+
                               |
                   3. deterministic incidents
                               |
                               v
                   +------------------------+
                   | Ollama Cloud API       |
                   | classify + summarize   |
                   | one incident at a time |
                   +-----------+------------+
                               |
                   4. dedupe by fingerprint
                               |
                               v
                   +------------------------+
                   | GitHub Issues          |
                   | search / create /      |
                   | comment on recurrence  |
                   +------------------------+
```

## What We Actually Need

| Capability | Current-ai bender | Log Watcher for PatchNotes |
|---|---|---|
| Trigger | Trigger.dev scheduled task | Azure Functions timer trigger |
| Log source | BetterStack MCP | Application Insights / Azure Monitor query |
| LLM behavior | Multi-turn agent | Single-shot structured analysis |
| Issue tracker | Linear | GitHub Issues |
| Repo context | Full clone + tools | None |
| Dedupe | Agent reasoning | Deterministic fingerprint in code |
| Cost | High relative to project size | Near-zero to low |

## Design

### Component 1: Scheduling and Run State

Use a new Azure Function alongside `SyncTimerFunction`:

```csharp
[Function("LogWatcher")]
public async Task Run(
    [TimerTrigger("0 0 * * * *")] TimerInfo timer,
    CancellationToken cancellationToken)
```

Default behavior:

- Run **once per hour**
- Query from the last successful watermark to `now`
- Include a small overlap window, for example 5 minutes, to tolerate clock skew and delayed ingestion
- Persist the watermark after a successful run

The key requirement is **stateful processing**, not `ago(60m)` queries on every execution. Without a persisted watermark, retries and delayed runs will create both gaps and duplicates.

**Recommendation:** store the checkpoint in the PatchNotes database as a single-row `LogWatcherCheckpoint` record. The project already has a database and this keeps scheduling state durable and testable.

### Component 2: Log Ingestion

Start with a narrow query scope:

- `exceptions` table: unhandled exceptions and stack traces
- `requests` table: failed requests (`resultCode >= 500`)

Do **not** start with `traces`. Trace tables are usually the fastest path to noise, prompt bloat, and weak issue quality.

The API and sync function share a single Application Insights resource. Differentiate them using `cloud_RoleName` in the query (e.g., `where cloud_RoleName in ("api-myreleasenotes-ai", "fn-patchnotes-sync")`).

Out of scope for v1:

- Frontend telemetry
- Email-function telemetry
- Warning-level traces

The query service should hide whether the underlying endpoint is the classic Application Insights query API or Azure Monitor query API. That keeps the feature aligned with the repo's telemetry migration work without blocking on it.

**What to extract per grouped incident:**

- `cloud_RoleName` (identifies API vs sync function)
- Exception type
- Normalized exception message
- Operation name / route
- Top app stack frame or method name
- Occurrence count in the time window
- First seen / last seen timestamps
- A representative sample event ID or operation ID for lookup

### Component 3: Sanitization and Normalization

This happens **before** any LLM call and **before** any GitHub issue creation.

Redact or discard:

- Email addresses
- Authorization headers
- Bearer tokens, API keys, cookies, secrets
- Full query strings
- Request and response bodies
- Connection strings
- Large raw stack traces

Normalize:

- Collapse IDs and GUID-like values when they are not part of the root cause
- Keep only the top 3-5 relevant application frames
- Trim repeated message noise and framework boilerplate
- Keep links back to telemetry rather than embedding large raw payloads

The GitHub issue should contain only sanitized details plus enough structured metadata to find the original telemetry.

### Component 4: Deterministic Grouping and Fingerprinting

The LLM should **not** be responsible for grouping or dedupe.

Group incidents in code using normalized fields such as:

- Source name
- Exception type
- Normalized operation name
- Top application frame
- Result-code bucket for failed requests

Compute a stable fingerprint from those fields, for example a SHA-256 hash over the normalized tuple.

Example fingerprint inputs:

```
api|NullReferenceException|GET /api/feed|PatchNotes.Api.FeedRoutes.GetFeed
sync|HttpRequestException|GitHubClient.GetReleasesAsync|5xx
```

Persist the fingerprint in the issue body using a machine-readable marker:

```markdown
<!-- logwatcher:fingerprint=abc123... -->
```

GitHub dedupe then becomes:

1. Search open issues labeled `ai-observed` for the fingerprint marker
2. If found, treat as recurrence
3. If not found, create a new issue

This is much more stable than an LLM-generated `search_key`.

### Component 5: LLM Analysis

Use Ollama Cloud only for tasks that benefit from language judgment:

- Severity classification
- Category classification
- Writing a short operator-facing summary
- Suggesting a concise issue title

It should **not** decide:

- Whether incidents are duplicates
- Whether data is safe to expose
- Whether a log sample should be grouped with another incident

Because the expected incident volume is low and the function runs hourly, analyze **one grouped incident at a time** instead of batching the full hour into one prompt. That keeps prompts small, retries cheap, and JSON parsing simpler.

#### AI Integration

The current `IAiClient` abstraction is release-summary specific. This feature should not pretend it can reuse `SummarizeReleaseNotesAsync` directly.

Recommended direction:

- Reuse the existing `AI` configuration and HTTP plumbing
- Add a dedicated `ILogAnalysisAiService` for this feature
- Optionally factor out a lower-level structured chat-completion method if that abstraction becomes useful elsewhere

#### Prompt shape

Input to the model should be a sanitized incident summary, not raw logs:

```json
{
  "source": "api",
  "fingerprint": "abc123",
  "exceptionType": "NullReferenceException",
  "message": "Object reference not set to an instance of an object",
  "operation": "GET /api/feed",
  "topFrame": "PatchNotes.Api.FeedRoutes.GetFeed",
  "occurrences": 18,
  "firstSeenUtc": "2026-04-08T12:00:00Z",
  "lastSeenUtc": "2026-04-08T12:58:00Z"
}
```

Expected JSON response:

```json
{
  "title": "Feed endpoint throws NullReferenceException",
  "severity": "high",
  "category": "api",
  "summary": "The feed endpoint is failing repeatedly due to a null reference in FeedRoutes.GetFeed. This likely affects users loading the homepage feed.",
  "confidence": 0.85
}
```

The `confidence` field is a 0–1 score reflecting how well the model understands the incident. **Only file an issue when confidence > 0.6.** Below that threshold, log the incident as skipped with the raw structured metadata so it can be reviewed manually. This prevents the LLM from generating vague or misleading issues when the error context is ambiguous.

Recommended model settings:

- Model: `gemma4:31b` — native structured JSON output, 256K context, drop-in successor to gemma3:27b. If free-tier GPU time becomes a constraint, `gemma4:26b` (MoE, 3.8B active params) is a lighter alternative with the same capabilities.
- Temperature: low (`0.1` to `0.3`)
- Max tokens: small response budget
- Use Ollama's `response_format` with a JSON schema to enforce structure at the engine level in addition to the model's native JSON support

### Component 6: GitHub Issue Filing

Use the GitHub REST API, not the `gh` CLI, from inside the Azure Function.

**Create a new issue when:**

- The incident fingerprint does not match an open `ai-observed` issue
- Occurrence count meets the threshold
- Severity meets the configured threshold
- LLM confidence > 0.6
- Dry-run mode is disabled

**Comment on an existing issue when:**

- The fingerprint matches an existing open issue
- The incident recurred
- The most recent watcher comment is older than the cooldown window

**Do not comment on every run.** A recurrence comment cooldown of 24 hours is a reasonable default.

#### Issue template

```markdown
[LogWatcher] {title}

## Incident
- Severity: {severity}
- Category: {category}
- Source: {source}
- Occurrences: {occurrences} in the last run window
- First seen: {firstSeenUtc}
- Last seen: {lastSeenUtc}

## Summary
{summary}

## Sanitized Details
- Exception type: `{exceptionType}`
- Operation: `{operation}`
- Top frame: `{topFrame}`
- Sample telemetry id: `{sampleEventId}`

## Notes
- Filed automatically by LogWatcher
- Raw logs remain in Application Insights / Azure Monitor

<!-- logwatcher:fingerprint={fingerprint} -->
```

#### Labels

- `ai-observed` always
- `severity:critical`, `severity:high`, `severity:medium`, `severity:low`
- `area:api`, `area:sync`, `area:auth`, `area:database`, `area:external`, `area:unknown`

The namespaced labels are easier to query and less likely to collide with other label usage later.

Labels are auto-created on first use: the issue filing service should ensure each required label exists before applying it (create via `POST /repos/{owner}/{repo}/labels`, ignore 422 if it already exists). This keeps the setup self-healing and avoids a manual bootstrap step.

#### Safety limits

Add hard caps so one noisy deployment cannot flood the repo:

- `MaxGroupsPerRun`: 10
- `MaxNewIssuesPerRun`: 3
- `MaxCommentsPerRun`: 5

Anything beyond the cap should be logged as deferred work, not silently ignored.

### Component 7: Function Logging and Observability

Use `ILogger` for watcher logs and metrics-like event data. Do not add new `TelemetryClient` dependencies to this feature.

Useful structured logs:

- run started / completed
- query window start and end
- incidents grouped
- incidents skipped by threshold
- new issue created
- recurrence comment added
- run cap reached
- AI parse failure or GitHub API failure

If the repo's OpenTelemetry migration lands first, this feature should follow that model automatically.

## Configuration

Reuse existing shared configuration for GitHub and AI:

- `GitHub:Token`
- `AI:BaseUrl`
- `AI:ApiKey`
- `AI:Model`

Add only watcher-specific settings under `LogWatcher`:

```json
{
  "LogWatcher": {
    "Enabled": false,
    "DryRun": true,
    "QueryWindowMinutes": 60,
    "QueryOverlapMinutes": 5,
    "MinOccurrences": 3,
    "SeverityThreshold": "high",
    "RecurrenceCommentCooldownHours": 24,
    "MaxGroupsPerRun": 10,
    "MaxNewIssuesPerRun": 3,
    "MaxCommentsPerRun": 5,
    "AppInsightsAppId": "",
    "AppInsightsApiKey": "",
    "CloudRoleNames": ["api-myreleasenotes-ai", "fn-patchnotes-sync"]
  }
}
```

If the query path later moves to Azure Monitor with managed identity, keep the `ILogQueryService` interface stable and change only its implementation and options.

## Project Structure

```text
PatchNotes.Functions/
  LogWatcherFunction.cs              # Timer trigger

PatchNotes.Sync.Core/
  LogWatcher/
    LogWatcherOrchestrator.cs        # End-to-end pipeline
    LogWatcherOptions.cs             # Configuration POCO
    ILogQueryService.cs
    LogQueryService.cs
    IIncidentGrouper.cs
    IncidentGrouper.cs
    ISanitizer.cs
    TelemetrySanitizer.cs
    ILogAnalysisAiService.cs
    LogAnalysisAiService.cs
    IIssueFilingService.cs
    GitHubIssueFilingService.cs
    IncidentFingerprint.cs
    LogIncident.cs
    LogWatcherCheckpoint.cs

PatchNotes.Data/
  ...                                # Checkpoint persistence if DB-backed

PatchNotes.Tests/
  LogWatcher/
    IncidentGrouperTests.cs
    TelemetrySanitizerTests.cs
    LogAnalysisAiServiceTests.cs
    GitHubIssueFilingServiceTests.cs
    LogWatcherOrchestratorTests.cs
```

The core behavior belongs in `PatchNotes.Sync.Core` so it can be tested without the Functions host.

## Cost Analysis

With an hourly schedule, the baseline volume is low:

- 24 runs per day
- Often zero or a handful of grouped incidents per run
- One small LLM call per grouped incident

Estimated cost envelope:

| Component | Cost |
|---|---|
| Ollama Cloud free tier | Likely sufficient initially |
| Ollama Cloud Pro tier | Fallback if volume grows |
| Azure Functions | Already deployed |
| App Insights / Azure Monitor query | Existing telemetry resource |
| GitHub API | Well within authenticated limits |
| **Total** | **Near-zero to low monthly cost** |

## Rollout Plan

### Phase 1: Dry-run only

- Implement querying, sanitization, grouping, and fingerprinting
- Run hourly in `DryRun: true`
- Log what would have been filed
- Start with `exceptions` and failed `requests` from API and sync only
- Tune thresholds for at least several days before enabling issue creation

### Phase 2: Limited issue filing

- Enable GitHub filing
- Threshold: `high` and `critical` only
- Keep per-run caps low
- Create labels up front
- Add recurrence comments with a 24-hour cooldown

### Phase 3: Refinement

- Lower severity threshold if signal quality is good
- Add optional `traces` support only if it improves coverage
- Consider slower cadence if hourly still feels too chatty
- Consider email or admin notification for `critical` incidents
- Consider auto-closing watcher issues after long quiet periods

## Follow-Up Checklist

- [ ] Decide the initial telemetry query backend and auth path
- [ ] Add durable checkpoint persistence for `LogWatcher`
- [ ] Implement sanitization and deterministic incident grouping
- [ ] Implement `ILogAnalysisAiService` with structured JSON parsing
- [ ] Implement GitHub issue create/comment flows using fingerprint-based dedupe
- [ ] Add caps, thresholds, and dry-run behavior to the orchestrator
- [ ] Add unit tests for grouping, sanitization, AI parsing, and issue filing
- [ ] Run the watcher in dry-run mode for several days and tune thresholds before enabling issue creation

## Open Questions

1. **Checkpoint storage:** database row vs another durable store
   - Recommendation: use the PatchNotes database first

2. **Telemetry query backend:** classic App Insights API vs Azure Monitor query API
   - Recommendation: keep the interface abstract and choose the simplest implementation that matches current deployment

3. **Issue recurrence UX:** comment on recurrence vs update issue body
   - Recommendation: comment, but enforce a cooldown to avoid spam
