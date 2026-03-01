# Plan: Move Function Apps from Flex Consumption to App Service Plan

## Problem

The timer triggers on `fn-patchnotes-sync` and `fn-patchnotes-email` (both on Flex Consumption / FC1) are not firing reliably. Over the last 24h, each function ran only once instead of the expected 24 times (hourly). Flex Consumption scales to zero and the timer trigger lease mechanism is not waking the instances back up consistently.

## Current State

| Resource | SKU | Region |
|----------|-----|--------|
| `fn-patchnotes-sync` | Flex Consumption (FC1) | East US |
| `fn-patchnotes-email` | Flex Consumption (FC1) | East US |
| `api-myreleasenotes-ai` | B1 (`ASP-MyPkgUpdate-Linux`) | Central US |
| `myfreesqldbserver-tiny-tools` (SQL Server) | — | Central US |
| `stpatchnotessync` (Storage) | — | East US |
| `mypkgupdatest` (Storage) | — | Central US |
| `ai-patchnotes` (App Insights) | — | East US |

Key finding: The **database and API are both in Central US**. The functions in East US are the outliers.

## Recommended Approach: Move functions to Central US on existing B1 plan

Recreate both function apps in Central US on the existing `ASP-MyPkgUpdate-Linux` B1 plan.

**Pros:**
- No additional cost — reuses the existing B1 plan (~$13/month already being paid)
- Co-locates functions with the database and API (lower latency)
- B1 supports Always On — timer triggers fire reliably
- Simplifies the infrastructure (everything in one region, one plan)

**Cons:**
- Functions move away from `stpatchnotessync` storage (East US) — minor latency for timer lease blobs; can switch to `mypkgupdatest` (Central US) to avoid this
- App Insights is in East US — works fine cross-region, no impact
- B1 has limited resources (1 core, 1.75 GB RAM) — now shared between API + both functions
- Brief downtime during delete/recreate (acceptable for hourly batch jobs)

## Steps

### 1. Export current settings

```bash
# Sync function
az functionapp config appsettings list \
  --name fn-patchnotes-sync \
  --resource-group MyPkgUpdate > sync-settings.json

# Email function
az functionapp config appsettings list \
  --name fn-patchnotes-email \
  --resource-group MyPkgUpdate > email-settings.json
```

### 2. Delete Flex Consumption function apps

Flex Consumption apps cannot be moved to an App Service Plan directly — they must be deleted and recreated.

```bash
az functionapp delete --name fn-patchnotes-sync --resource-group MyPkgUpdate
az functionapp delete --name fn-patchnotes-email --resource-group MyPkgUpdate
```

### 3. Recreate on the existing B1 plan

```bash
# Sync function (.NET isolated)
az functionapp create \
  --name fn-patchnotes-sync \
  --resource-group MyPkgUpdate \
  --plan ASP-MyPkgUpdate-Linux \
  --runtime dotnet-isolated \
  --runtime-version 10 \
  --functions-version 4 \
  --os-type Linux \
  --storage-account mypkgupdatest

# Email function (Node.js)
az functionapp create \
  --name fn-patchnotes-email \
  --resource-group MyPkgUpdate \
  --plan ASP-MyPkgUpdate-Linux \
  --runtime node \
  --runtime-version 22 \
  --functions-version 4 \
  --os-type Linux \
  --storage-account mypkgupdatest
```

Note: Using `mypkgupdatest` (Central US) instead of `stpatchnotessync` (East US) to keep storage in the same region.

### 4. Restore app settings

```bash
az functionapp config appsettings set \
  --name fn-patchnotes-sync \
  --resource-group MyPkgUpdate \
  --settings @sync-settings.json

az functionapp config appsettings set \
  --name fn-patchnotes-email \
  --resource-group MyPkgUpdate \
  --settings @email-settings.json
```

### 5. Enable Always On

```bash
az functionapp config set \
  --name fn-patchnotes-sync \
  --resource-group MyPkgUpdate \
  --always-on true

az functionapp config set \
  --name fn-patchnotes-email \
  --resource-group MyPkgUpdate \
  --always-on true
```

### 6. Redeploy function app code

Trigger CI/CD pipelines or manually deploy both function apps. The deployment targets have changed region, so verify any region-specific deployment config in the pipelines.

### 7. Clean up old Flex Consumption plans

Once both apps are confirmed working:

```bash
az appservice plan delete --name ASP-MyPkgUpdate-f1df --resource-group MyPkgUpdate --yes
az appservice plan delete --name ASP-MyPkgUpdate-f43e --resource-group MyPkgUpdate --yes
```

Optionally delete the East US storage account if no longer needed:
```bash
# Only if nothing else uses it
az storage account delete --name stpatchnotessync --resource-group MyPkgUpdate --yes
```

### 8. Verify

- Monitor App Insights for the next few hours to confirm both functions fire every hour
- Check for `SyncReleasesCompleted` and `DigestCompleted` custom events
- Confirm Always On is active:
  ```bash
  az functionapp config show --name fn-patchnotes-sync --resource-group MyPkgUpdate --query alwaysOn
  az functionapp config show --name fn-patchnotes-email --resource-group MyPkgUpdate --query alwaysOn
  ```

## Risks

- **Downtime**: Both functions unavailable during delete/recreate. Acceptable for hourly batch jobs.
- **Settings drift**: Must carefully export/restore all app settings. Review the exported JSON before deleting.
- **CI/CD updates**: Deployment pipelines may reference Flex Consumption-specific config or the East US region.
- **B1 resource limits**: 1 core / 1.75 GB RAM now shared between the API and both functions. The sync function takes ~3.5 min per run — should be fine, but monitor memory usage after migration.
- **Storage account change**: Switching from `stpatchnotessync` to `mypkgupdatest` means timer trigger lease state resets. Functions may run immediately on first start (harmless for these workloads).
