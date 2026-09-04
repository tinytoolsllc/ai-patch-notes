# Running PatchNotes.Sync as an Azure Function

This document covers how to set up and deploy the `PatchNotes.Sync` tool as a timer-triggered Azure Function running on a cron schedule.

## Overview

Instead of running the sync tool manually or via a traditional cron job on a VM, we use an **Azure Functions Timer Trigger** to execute the sync on a schedule. This gives us serverless execution, built-in monitoring via Application Insights, and no infrastructure to manage.

## Azure Resource Setup

### Resource Group

All sync-related resources live in the same resource group as the existing PatchNotes infrastructure. If you need a new one:

```bash
az group create \
  --name rg-patchnotes \
  --location eastus
```

### Storage Account (required by Azure Functions)

Azure Functions requires a storage account for internal state (timer lease, execution logs).

```bash
az storage account create \
  --name stpatchnotessync \
  --resource-group rg-patchnotes \
  --location eastus \
  --sku Standard_LRS
```

### Function App

Create a .NET 10 (isolated worker) Function App on the **Consumption (Flex)** plan for cost-effective, schedule-driven execution:

```bash
az functionapp create \
  --name fn-patchnotes-sync \
  --resource-group rg-patchnotes \
  --storage-account stpatchnotessync \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --runtime-version 10 \
  --functions-version 4 \
  --os-type Linux
```

### Application Settings

Configure the environment variables the sync tool needs. These mirror the settings from the existing API deployment (see [ENV_VARIABLES.md](./ENV_VARIABLES.md)):

```bash
az functionapp config appsettings set \
  --name fn-patchnotes-sync \
  --resource-group rg-patchnotes \
  --settings \
    "ConnectionStrings__PatchNotes=<sql-server-connection-string>" \
    "GitHub__Token=<github-pat>" \
    "AI__ApiKey=<ai-api-key>" \
    "AI__BaseUrl=https://api.groq.com/openai/v1" \
    "AI__Model=llama-3.3-70b-versatile"
```

> **Tip:** For production, store secrets in Azure Key Vault and reference them with `@Microsoft.KeyVault(SecretUri=...)` syntax instead of setting values directly.

### Application Insights (recommended)

Enable monitoring to track sync execution, failures, and duration:

```bash
az monitor app-insights component create \
  --app ai-patchnotes-sync \
  --location eastus \
  --resource-group rg-patchnotes

az functionapp config appsettings set \
  --name fn-patchnotes-sync \
  --resource-group rg-patchnotes \
  --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=<connection-string>"
```

## Project Structure

The Azure Function project uses a `SyncPipeline` that runs sync and summary generation concurrently as a producer-consumer pipeline using `Channel<string>`. As soon as a package finishes syncing, its summaries start generating while the next package syncs.

```
PatchNotes.Functions/
├── PatchNotes.Functions.csproj
├── Program.cs              # Host builder & DI registration
├── SyncTimerFunction.cs    # Timer trigger function
├── host.json               # Function host configuration
└── local.settings.json     # Local development settings (gitignored)
```

### PatchNotes.Functions.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.50.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Timer" Version="4.3.1" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.0.7" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.ApplicationInsights" Version="2.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\PatchNotes.Data\PatchNotes.Data.csproj" />
    <ProjectReference Include="..\PatchNotes.Sync\PatchNotes.Sync.csproj" />
  </ItemGroup>

</Project>
```

> **Note:** .NET 10 requires `Microsoft.Azure.Functions.Worker` >= 2.50.0 and `Microsoft.Azure.Functions.Worker.Sdk` >= 2.0.5.

### Program.cs (Host Setup)

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PatchNotes.Data;
using PatchNotes.Data.AI;
using PatchNotes.Data.GitHub;
using PatchNotes.Sync;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.ConfigureFunctionsApplicationInsights();

        services.AddPatchNotesDbContext(context.Configuration);

        services.AddGitHubClient(options =>
        {
            var token = context.Configuration["GitHub:Token"];
            if (!string.IsNullOrEmpty(token))
                options.Token = token;
        });

        services.AddAiClient(options =>
        {
            var section = context.Configuration.GetSection(AiClientOptions.SectionName);
            var baseUrl = section["BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
                options.BaseUrl = baseUrl;
            options.ApiKey = section["ApiKey"];
            var model = section["Model"];
            if (!string.IsNullOrEmpty(model))
                options.Model = model;
        });

        services.AddTransient<ChangelogResolver>();
        services.AddTransient<VersionGroupingService>();
        services.AddTransient<SyncService>();
        services.AddTransient<SummaryGenerationService>();
        services.AddTransient<SyncPipeline>();
    })
    .Build();

host.Run();
```

### SyncTimerFunction.cs

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PatchNotes.Sync;

namespace PatchNotes.Functions;

public class SyncTimerFunction(
    SyncPipeline pipeline,
    ILogger<SyncTimerFunction> logger)
{
    // Runs every 6 hours: at midnight, 6am, noon, 6pm UTC
    [Function("SyncReleases")]
    public async Task Run(
        [TimerTrigger("0 0 */6 * * *")] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Sync function triggered at {Time}", DateTime.UtcNow);

        var result = await pipeline.RunAsync(cancellationToken);

        logger.LogInformation(
            "Sync pipeline complete: {Packages} packages synced, {Releases} releases added, " +
            "{Summaries} summaries generated, {SyncErrors} sync errors, {SummaryErrors} summary errors",
            result.PackagesSynced,
            result.ReleasesAdded,
            result.SummariesGenerated,
            result.SyncErrors.Count,
            result.SummaryErrors.Count);

        if (timerInfo.ScheduleStatus is not null)
        {
            logger.LogInformation("Next sync scheduled at {NextRun}", timerInfo.ScheduleStatus.Next);
        }
    }
}
```

The function uses `SyncPipeline` which runs sync and summary generation concurrently via a bounded `Channel<string>`. Each side gets its own DI scope (via `IServiceScopeFactory`) for DbContext thread safety.

### host.json

```json
{
  "version": "2.0",
  "logging": {
    "applicationInsights": {
      "samplingSettings": {
        "isEnabled": true,
        "excludedTypes": "Request"
      },
      "enableLiveMetrics": true
    },
    "logLevel": {
      "default": "Information",
      "Function.SyncReleases": "Information"
    }
  },
  "functionTimeout": "00:10:00"
}
```

The `functionTimeout` is set to 10 minutes (the Consumption plan maximum). If syncing all packages regularly exceeds this, consider switching to the **Flex Consumption** or **Premium** plan.

### local.settings.json (for local development)

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ConnectionStrings__PatchNotes": "Data Source=patchnotes.db",
    "GitHub__Token": "<your-github-pat>",
    "AI__ApiKey": "<your-ai-key>",
    "AI__BaseUrl": "https://api.groq.com/openai/v1",
    "AI__Model": "llama-3.3-70b-versatile"
  }
}
```

## Cron Schedule

The timer trigger uses a **6-field NCrontab expression** (`{second} {minute} {hour} {day} {month} {day-of-week}`):

| Schedule         | Expression       | Description                  |
| ---------------- | ---------------- | ---------------------------- |
| Every 6 hours    | `0 0 */6 * * *`  | Midnight, 6am, noon, 6pm UTC |
| Every hour       | `0 0 * * * *`    | Top of every hour            |
| Twice daily      | `0 0 6,18 * * *` | 6am and 6pm UTC              |
| Once daily (3am) | `0 0 3 * * *`    | 3:00 AM UTC                  |

The schedule is configured in `SyncTimerFunction.cs` via the `TimerTrigger` attribute. To change it without redeploying, use an app setting:

```csharp
[TimerTrigger("%SyncSchedule%")] TimerInfo timerInfo
```

Then set the `SyncSchedule` app setting on the Function App:

```bash
az functionapp config appsettings set \
  --name fn-patchnotes-sync \
  --resource-group rg-patchnotes \
  --settings "SyncSchedule=0 0 */6 * * *"
```

## Deployment

### Option 1: GitHub Actions (recommended)

Add a job to the existing `deploy.yml` workflow:

```yaml
build-sync:
  name: Build Sync Function
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v6

    - name: Setup .NET
      uses: actions/setup-dotnet@v5
      with:
        dotnet-version: "10.0.x"

    - name: Publish Function
      run: dotnet publish PatchNotes.Functions/PatchNotes.Functions.csproj --configuration Release --output ./publish/functions

    - name: Upload artifact
      uses: actions/upload-artifact@v6
      with:
        name: functions
        path: ./publish/functions

deploy-sync:
  name: Deploy Sync Function
  needs: [build-sync, migrate-database]
  runs-on: ubuntu-latest
  environment:
    name: production
  steps:
    - name: Download artifact
      uses: actions/download-artifact@v7
      with:
        name: functions
        path: ./functions

    - name: Login to Azure
      uses: azure/login@v2
      with:
        client-id: ${{ secrets.AZURE_CLIENT_ID }}
        tenant-id: ${{ secrets.AZURE_TENANT_ID }}
        subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

    - name: Deploy to Azure Functions
      uses: azure/functions-action@v2
      with:
        app-name: fn-patchnotes-sync
        package: ./functions
```

### Option 2: Azure CLI (manual)

```bash
cd PatchNotes.Functions
dotnet publish --configuration Release --output ./publish
cd publish && zip -r ../deploy.zip .
az functionapp deployment source config-zip \
  --name fn-patchnotes-sync \
  --resource-group rg-patchnotes \
  --src ../deploy.zip
```

## Monitoring

Once deployed, monitor the function via:

- **Azure Portal** > Function App > `fn-patchnotes-sync` > Monitor
- **Application Insights** > Live Metrics / Failures / Logs
- **Log query** (KQL):

  ```kusto
  traces
  | where message has "Sync"
  | order by timestamp desc
  | take 50
  ```

Failed executions are automatically retried by the Functions runtime on the next scheduled run. The timer trigger is singleton by default, so overlapping executions are prevented.
