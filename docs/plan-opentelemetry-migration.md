# Plan: Migrate to OpenTelemetry

## Motivation

The Application Insights SDK is being deprecated in favor of OpenTelemetry. The .NET Functions project (`PatchNotes.Functions`) is additionally blocked: it's pinned to `Microsoft.ApplicationInsights.WorkerService` 2.23.0 because 3.x removes `ITelemetryInitializer`, which crashes the isolated worker. Migrating all projects to OpenTelemetry resolves this version pin and aligns the entire stack with the supported path forward.

References:
- Azure Functions: https://learn.microsoft.com/en-us/azure/azure-functions/opentelemetry-howto?tabs=app-insights%2Cihostapplicationbuilder&pivots=programming-language-csharp
- ASP.NET Core: https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-enable?tabs=aspnetcore
- Node.js: https://learn.microsoft.com/en-us/azure/azure-functions/opentelemetry-howto?tabs=app-insights&pivots=programming-language-typescript

## Scope

All three projects, migrated and deployed in phases:

### Phase 1 — `PatchNotes.Functions` (.NET isolated worker)
Highest priority. Resolves the incompatible NuGet package and validates the OTel pipeline end-to-end.

### Phase 2 — `PatchNotes.Api` (ASP.NET Core)
Low risk. No `TelemetryClient` usage — only `ILogger`. Swap one package and one line of registration code. Deploy after Phase 1 is verified in staging.

### Phase 3 — `patchnotes-email` (Node.js Azure Functions)
Most involved. Has a custom `telemetry.ts` wrapper with `trackEvent`/`trackException`/`flush` used across 4 function files plus tests. Deploy independently after Phases 1–2 are stable.

## Phase 1 — PatchNotes.Functions

### 1.1. Update NuGet packages

In `PatchNotes.Functions/PatchNotes.Functions.csproj`:

**Remove:**
- `Microsoft.ApplicationInsights.WorkerService` 2.23.0
- `Microsoft.Azure.Functions.Worker.ApplicationInsights` 2.50.0
- The `<!-- DO NOT upgrade to 3.x -->` comment

**Add:**
- `Microsoft.Azure.Functions.Worker.OpenTelemetry`
- `OpenTelemetry.Extensions.Hosting`
- `Azure.Monitor.OpenTelemetry.Exporter`

### 1.2. Remove TelemetryClient and Console.WriteLine across the project

This is a telemetry schema change, not just a package swap. All `TelemetryClient` usage and `Console.WriteLine` calls are removed and replaced by structured `ILogger` calls. Data that was previously in `AppEvents` (custom events) will now appear in `AppTraces` (structured logs).

**Decision: structured logs, not custom spans.** The existing `ILogger` calls already emit the same data as every `TrackEvent` call — dropping `TelemetryClient` means removing duplicate code, not adding a new abstraction. Custom spans (`ActivitySource`) would add registration boilerplate and the nullable `activity?.SetTag` pattern for what are fundamentally point-in-time signals, not operations that need duration tracking or trace context propagation. If distributed tracing across the sync pipeline is wanted later (flamechart of SyncReleases → per-package sync → changelog resolution → summary generation), that's a separate initiative.

**In `Program.cs`:**

Replace the App Insights registration:

```csharp
// Remove
using Microsoft.ApplicationInsights;

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Add
using Azure.Monitor.OpenTelemetry.Exporter;

builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults()
    .UseAzureMonitorExporter();
```

Remove the `TelemetryClient` startup event (lines 60-66). The `SyncFunctionStarted` custom event is replaced by the existing `logger.LogError` calls for missing config keys. If a positive startup signal is needed, add a single `logger.LogInformation("SyncFunction started, configValid={Valid}", missingKeys.Count == 0)`.

**In `SyncTimerFunction.cs`:**

Remove the `TelemetryClient` dependency injection and all `TrackEvent`/`Flush` calls:

- `TrackEvent("SyncReleasesCompleted", ...)` — already duplicated by `logger.LogInformation(...)` on line 35
- `TrackEvent("SyncReleasesFailed", ...)` — already duplicated by `logger.LogError(...)` on line 76
- `telemetryClient.Flush()` + `Task.Delay(2s)` — not needed with OTel; the exporter handles batching and flush on shutdown

Remove all `Console.WriteLine(...)` calls. These don't produce App Insights telemetry today, but in OTel mode the Functions host captures stdout and forwards it to the telemetry pipeline, which would create new duplicates.

### 1.3. Update host.json

```json
{
    "version": "2.0",
    "telemetryMode": "OpenTelemetry",
    "logging": {
        "logLevel": {
            "default": "Information",
            "Function.SyncReleases": "Information",
            "PatchNotes": "Information"
        }
    },
    "functionTimeout": "00:10:00"
}
```

- Add `"telemetryMode": "OpenTelemetry"` at root level
- Remove the `logging.applicationInsights` section (not applicable in OTel mode)

### 1.4. Document sampling behavior

Currently sampling is disabled (`samplingSettings.isEnabled: false`). No extra app setting is required to preserve that behavior in the .NET OpenTelemetry pipeline; Azure Monitor OpenTelemetry does not enable sampling by default.

Only add an `OTEL_TRACES_SAMPLER` setting if we intentionally want sampling later.

If sampling is enabled in the future, document the operational consequence: logs associated with unsampled traces are dropped by default unless log sampling is configured differently.

No change needed for `APPLICATIONINSIGHTS_CONNECTION_STRING` — the Azure Monitor exporter reads it automatically.

### 1.5. Verify SyncNewPackagesFunction.cs

This function only uses `ILogger`, no `TelemetryClient`. No changes needed, but confirm after migration that logs still flow to App Insights.

## Phase 2 — PatchNotes.Api

### 2.1. Update NuGet packages

In `PatchNotes.Api/PatchNotes.Api.csproj`:

**Remove:**
- `Microsoft.ApplicationInsights.AspNetCore` 3.0.0

**Add:**
- `Azure.Monitor.OpenTelemetry.AspNetCore`

This is the full Azure Monitor distro for ASP.NET Core. It includes Live Metrics by default.

### 2.2. Update Program.cs

Replace the App Insights registration:

```csharp
// Remove
builder.Services.AddApplicationInsightsTelemetry();

// Add
using Azure.Monitor.OpenTelemetry.AspNetCore;

builder.Services.AddOpenTelemetry().UseAzureMonitor();
```

The conditional `IsProduction() || IsStaging()` guard and `APPLICATIONINSIGHTS_CONNECTION_STRING` check can remain as-is.

No other code changes needed — the API project uses only `ILogger`, no `TelemetryClient`.

### 2.3. Verify

- Confirm request telemetry appears in App Insights (AppRequests table)
- Confirm structured logs flow (AppTraces table)
- Confirm exception tracking works (AppExceptions table)
- Check for duplicate request telemetry — the distro includes `AspNetCoreInstrumentation` automatically. If duplicates appear, this is the known issue from the MS docs; switch to the bare `Azure.Monitor.OpenTelemetry.Exporter` instead of the distro.

## Phase 3 — patchnotes-email (Node.js)

### 3.1. Update npm packages

**Remove:**
- `applicationinsights` (v3.14.0)

**Add:**
- `@opentelemetry/api`
- `@opentelemetry/auto-instrumentations-node`
- `@azure/monitor-opentelemetry-exporter`
- `@azure/functions-opentelemetry-instrumentation`

### 3.2. Replace telemetry.ts

The current `src/lib/telemetry.ts` wraps the App Insights SDK with `trackEvent`, `trackException`, and `flush`. Rewrite it to use the OpenTelemetry API:

```typescript
// src/lib/telemetry.ts
import { AzureFunctionsInstrumentation } from '@azure/functions-opentelemetry-instrumentation';
import { AzureMonitorLogExporter, AzureMonitorTraceExporter } from '@azure/monitor-opentelemetry-exporter';
import { getNodeAutoInstrumentations, getResourceDetectors } from '@opentelemetry/auto-instrumentations-node';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { detectResourcesSync } from '@opentelemetry/resources';
import { LoggerProvider, SimpleLogRecordProcessor } from '@opentelemetry/sdk-logs';
import { NodeTracerProvider, SimpleSpanProcessor } from '@opentelemetry/sdk-trace-node';

const resource = detectResourcesSync({ detectors: getResourceDetectors() });

const tracerProvider = new NodeTracerProvider({ resource });
tracerProvider.addSpanProcessor(new SimpleSpanProcessor(new AzureMonitorTraceExporter()));
tracerProvider.register();

const loggerProvider = new LoggerProvider({ resource });
loggerProvider.addLogRecordProcessor(new SimpleLogRecordProcessor(new AzureMonitorLogExporter()));

registerInstrumentations({
    tracerProvider,
    loggerProvider,
    instrumentations: [getNodeAutoInstrumentations(), new AzureFunctionsInstrumentation()],
});
```

**Decision: same as Phase 1 — structured logs, not custom spans.** Replace the `trackEvent`/`trackException`/`flush` wrapper functions with `console.log`/`console.error` structured logging. The OTel pipeline captures these automatically.

The custom events that will move from `AppEvents` to `AppTraces`:
- `EmailFunctionStarted` (cold start in telemetry.ts)
- `DigestCompleted`, `DigestEmailFailed` (sendDigest.ts)
- `WelcomeEmailSent`, `WelcomeEmailFailed` (sendWelcome.ts)
- `TestEmailSent`, `TestEmailFailed` (sendTestEmail.ts)
- `PreviewRendered`, `PreviewRenderFailed` (renderPreview.ts)

### 3.3. Update function entry point

Update `package.json` main field to include the telemetry initialization:

```json
"main": "dist/src/{lib/telemetry.js,functions/*.js}"
```

Or create a separate `src/index.ts` that imports the telemetry setup and re-exports, per the MS docs.

### 3.4. Update host.json

```json
{
    "version": "2.0",
    "telemetryMode": "OpenTelemetry",
    "logging": {
        "logLevel": {
            "default": "Information",
            "Function.sendDigest": "Information",
            "Function.sendWelcome": "Information",
            "Function.sendTestEmail": "Information"
        }
    },
    "extensionBundle": {
        "id": "Microsoft.Azure.Functions.ExtensionBundle",
        "version": "[4.*, 5.0.0)"
    }
}
```

### 3.5. Update tests

`sendTestEmail.test.ts` mocks `trackEvent`, `trackException`, and `flush` from `../lib/telemetry.js`. These mocks need to be updated or removed depending on how the replacement logging is structured.

### 3.6. Verify

- Confirm email function invocations appear in App Insights
- Confirm structured log properties (recipient counts, error details) are searchable
- Confirm the `sendDigest` timer trigger fires correctly with telemetry

## Testing

Each phase should be deployed and verified independently before starting the next.

### Phase 1 testing
1. Run locally with `func start` and verify logs appear in console
2. Deploy to staging and confirm telemetry appears in App Insights:
   - Function invocation traces under "Recent function invocations"
   - Structured log properties (packagesSynced, releasesAdded, etc.) in log search
   - No duplicate entries after removing `Console.WriteLine(...)`
3. Verify the hourly `SyncReleases` timer fires and completes with telemetry

### Phase 2 testing
1. Run locally and confirm request logging works
2. Deploy to staging and check for duplicate request telemetry (the known distro issue)
3. Verify exception tracking by triggering a known error path

### Phase 3 testing
1. Run locally with `func start` and verify email function logs appear
2. Deploy to staging and trigger a test email
3. Verify digest timer function completes with telemetry

### Cross-phase testing
- `/check-azure` skill must be updated to query `AppTraces` instead of `AppEvents` for the custom events that moved. This should be done as part of Phase 1, since the sync function events are the first to move.

## Operational Notes

- This migration intentionally removes the current custom-event telemetry shape from all projects. Data moves from `AppEvents` to `AppTraces`.
- The `/check-azure` Claude skill queries `AppEvents` for specific event names (`SyncFunctionStarted`, `SyncReleasesCompleted`, `SyncReleasesFailed`, `EmailFunctionStarted`, `DigestCompleted`, `WelcomeEmailSent`, `WelcomeEmailFailed`). These queries must be rewritten as part of the migration — not after.
- `host.json` live-metrics settings from the classic Application Insights section do not carry over directly to OpenTelemetry mode. For the API (Phase 2), the full distro includes Live Metrics by default. For the Functions projects (Phases 1 and 3), Live Metrics is not critical for timer/queue-triggered workloads.

## Rollback

Each phase can be rolled back independently:

- **Phase 1**: Revert `PatchNotes.Functions/` files (`csproj`, `Program.cs`, `SyncTimerFunction.cs`, `host.json`) and redeploy.
- **Phase 2**: Revert `PatchNotes.Api/` files (`csproj`, `Program.cs`) and redeploy.
- **Phase 3**: Revert `patchnotes-email/` files (`package.json`, `telemetry.ts`, `host.json`, function files) and redeploy.

`APPLICATIONINSIGHTS_CONNECTION_STRING` doesn't change in any phase, so the old SDKs reconnect immediately on rollback.
