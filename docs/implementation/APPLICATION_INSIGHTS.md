# Application Insights Setup

This document covers adding Application Insights monitoring to the PatchNotes API and Sync Function using a single shared workspace.

## Why

- **Request tracing** — every API call logged with duration, status code, and route
- **Dependency tracking** — automatic instrumentation of SQL queries, outbound HTTP calls (GitHub API, Groq), and EF Core
- **Exception logging** — unhandled exceptions captured with full stack traces
- **Live metrics** — real-time view of request rates, failures, and server health
- **Correlated traces** — follow a single request from API entry through database and external calls
- **Alerting** — configure alerts on failure rates, response times, or custom metrics

## Architecture

```
┌─────────────────┐     ┌──────────────────────┐
│  PatchNotes.Api  │────▶│                      │
│  (App Service)   │     │  Application Insights │
└─────────────────┘     │  (ai-patchnotes)      │
                        │                      │
┌─────────────────┐     │  Shared workspace     │
│  PatchNotes.Sync │────▶│  for all services     │
│  (Azure Function)│     │                      │
└─────────────────┘     └──────────────────────┘
```

Both services report to the same Application Insights resource, giving a unified view across the API and sync function. The frontend is excluded — see [Frontend](#frontend) for rationale.

## Step 1: Create the Application Insights Resource

Create a single resource that both services will share:

```bash
az monitor app-insights component create \
  --app ai-patchnotes \
  --location eastus \
  --resource-group rg-patchnotes \
  --kind web
```

Save the connection string from the output — you'll need it for both services:

```bash
az monitor app-insights component show \
  --app ai-patchnotes \
  --resource-group rg-patchnotes \
  --query connectionString \
  --output tsv
```

## Step 2: Configure the API (PatchNotes.Api)

### Add the NuGet package

```bash
dotnet add PatchNotes.Api/PatchNotes.Api.csproj \
  package Microsoft.ApplicationInsights.AspNetCore
```

### Register in Program.cs

Add a single line before `builder.Services.AddOpenApi()`:

```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

That's it. The SDK automatically:

- Reads the connection string from `APPLICATIONINSIGHTS_CONNECTION_STRING`
- Instruments all incoming HTTP requests
- Tracks EF Core queries and outbound HTTP calls (GitHub, Groq, Stytch, Stripe)
- Captures unhandled exceptions
- Collects performance counters (CPU, memory, request rate)

### Set the connection string on App Service

```bash
az webapp config appsettings set \
  --name api-mypkgupdate-com \
  --resource-group rg-patchnotes \
  --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=<connection-string>"
```

### Configure sampling (optional)

By default the SDK uses adaptive sampling to keep costs manageable. To customize, add to `appsettings.json`:

```json
{
  "ApplicationInsights": {
    "EnableAdaptiveSampling": true,
    "ConnectionString": ""
  },
  "Logging": {
    "ApplicationInsights": {
      "LogLevel": {
        "Default": "Warning"
      }
    }
  }
}
```

The `ConnectionString` in `appsettings.json` can be left empty — the environment variable takes precedence in production, and locally you can omit it to disable telemetry during development.

## Step 3: Configure the Sync Function (PatchNotes.Functions)

This is already outlined in [AZURE_FUNCTION_SYNC.md](./AZURE_FUNCTION_SYNC.md). The key pieces:

### NuGet package

```xml
<PackageReference Include="Microsoft.ApplicationInsights.WorkerService" Version="2.22.0" />
```

### Register in Program.cs

```csharp
services.AddApplicationInsightsTelemetryWorkerService();
services.ConfigureFunctionsApplicationInsights();
```

### Set the connection string on the Function App

```bash
az functionapp config appsettings set \
  --name fn-patchnotes-sync \
  --resource-group rg-patchnotes \
  --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=<connection-string>"
```

## Step 4: Add to CI/CD

Add the connection string as a GitHub Actions secret (`APPINSIGHTS_CONNECTION_STRING`) and set it during deployment. Add this step to both the `deploy-api` and `deploy-sync` jobs in `deploy.yml`:

```yaml
- name: Set Application Insights connection string
  uses: azure/appservice-settings@v1
  with:
    app-name: api-mypkgupdate-com # or fn-patchnotes-sync
    app-settings-json: |
      [
        {
          "name": "APPLICATIONINSIGHTS_CONNECTION_STRING",
          "value": "${{ secrets.APPINSIGHTS_CONNECTION_STRING }}",
          "slotSetting": false
        }
      ]
```

Alternatively, if app settings are already managed through the Azure CLI commands or Terraform, just ensure the connection string is included there.

## Step 5: Verify

After deploying, verify telemetry is flowing:

1. **Azure Portal** > Application Insights > `ai-patchnotes` > Live Metrics — you should see requests appearing in real time
2. **Transaction search** — search for a specific API endpoint to see the full request trace
3. **Application map** — visual dependency graph showing API > SQL Server, API > GitHub, etc.
4. **Failures** — any unhandled exceptions will appear here with stack traces

### Useful KQL queries

**Slowest API endpoints (last 24h):**

```kusto
requests
| where timestamp > ago(24h)
| summarize avg(duration), p95=percentile(duration, 95), count() by name
| order by p95 desc
```

**Failed dependency calls (GitHub/Groq):**

```kusto
dependencies
| where success == false
| where timestamp > ago(24h)
| summarize count() by target, type, resultCode
| order by count_ desc
```

**Sync function execution history:**

```kusto
requests
| where name == "SyncReleases"
| project timestamp, duration, success, resultCode
| order by timestamp desc
| take 20
```

## Step 6: Alerts (recommended)

Set up basic alerts to catch issues early:

```bash
# Alert on API failure rate > 5% over 5 minutes
az monitor metrics alert create \
  --name "api-high-failure-rate" \
  --resource-group rg-patchnotes \
  --scopes <app-insights-resource-id> \
  --condition "avg requests/failed > 5" \
  --window-size 5m \
  --description "API failure rate exceeded 5%"
```

At minimum, consider alerts for:

- API failure rate spike
- Sync function failures (any execution returning error)
- Response time degradation (p95 > threshold)

## Frontend

Application Insights is **not recommended** for the frontend SPA. Reasons:

- Adds ~30KB to the bundle for the JavaScript SDK
- Sends telemetry to a Microsoft-hosted endpoint, adding a third-party dependency
- Most meaningful errors will surface through the API telemetry anyway (failed requests, slow responses)
- Client-side exceptions (rendering errors, JS crashes) are better handled by a lightweight tool like Sentry if needed later

If client-side monitoring becomes necessary, consider the [Application Insights JavaScript SDK](https://learn.microsoft.com/en-us/azure/azure-monitor/app/javascript-sdk) or a dedicated frontend error tracker.

## Cost

Application Insights charges based on data ingestion volume. For a low-to-moderate traffic app:

- **First 5 GB/month** is free
- Adaptive sampling (enabled by default) keeps volume in check
- The Consumption plan Function App adds negligible telemetry (runs a few times per day)

For most projects at this scale, the free tier is sufficient.
