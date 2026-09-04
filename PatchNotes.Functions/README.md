# PatchNotes.Functions

Azure Function App that runs the sync pipeline on a schedule. Deployed to **fn-patchnotes-sync** (Flex Consumption, East US).

## Functions

### SyncReleases

Timer trigger that runs every 6 hours (`0 0 */6 * * *` — midnight, 6am, noon, 6pm UTC).

Executes the `SyncPipeline` which:

1. **Syncs releases** from GitHub for all tracked packages (producer)
2. **Generates AI summaries** for new release groups (consumer)

These run concurrently as a producer-consumer pipeline using `System.Threading.Channels`.

### SyncNewPackages

HTTP POST trigger (`/api/sync-new-packages`) that syncs packages which have never been fetched (`LastFetchedAt == null`). Called by the API in a fire-and-forget fashion after a new package is created via the watchlist. The 6-hour timer acts as a safety net if the ping fails.

**Network access**: This endpoint must **not** be publicly accessible. The Function App's access restrictions allow only the `AppService.CentralUS` service tag (matching the API's region) with a default Deny rule. The function also requires a function key (`AuthorizationLevel.Function`).

## Configuration

| Setting                        | Description                       |
| ------------------------------ | --------------------------------- |
| `ConnectionStrings:PatchNotes` | SQL Server connection string      |
| `GitHub:Token`                 | GitHub PAT for API access         |
| `AI:BaseUrl`                   | AI provider base URL              |
| `AI:ApiKey`                    | AI provider API key               |
| `AI:Model`                     | AI model name (e.g. `gemma4:31b`) |

In Azure, these are set as Function App application settings with `__` as the separator (e.g. `ConnectionStrings__PatchNotes`).

### API app settings (for the SyncNewPackages ping)

The API needs these settings to ping the Function after creating a new package:

| Setting            | Description                                                                                                    |
| ------------------ | -------------------------------------------------------------------------------------------------------------- |
| `SyncFunction:Url` | Full URL of the SyncNewPackages endpoint: `https://fn-patchnotes-sync.azurewebsites.net/api/sync-new-packages` |
| `SyncFunction:Key` | Function key from Azure Portal: Functions > SyncNewPackages > Function Keys > default                          |

## Local Development

1. Create `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ConnectionStrings__PatchNotes": "Data Source=patchnotes.db",
    "GitHub__Token": "<your-github-pat>",
    "AI__ApiKey": "<your-ai-key>",
    "AI__Model": "gemma4:31b"
  }
}
```

2. Run with Azure Functions Core Tools:

```bash
cd PatchNotes.Functions
func start
```

## Infrastructure

- **Plan**: Flex Consumption (Linux)
- **Runtime**: .NET 10 isolated worker
- **Monitoring**: Application Insights (shared with the API)
- **Timeout**: 10 minutes (`host.json`)
- **Deploy**: GitHub Actions pushes to `main` trigger build and deploy via `azure/functions-action@v1`
