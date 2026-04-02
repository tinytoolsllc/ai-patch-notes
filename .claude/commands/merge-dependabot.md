# Merge Dependabot Branches

Merge all pending dependabot PRs into the main branch, validate, and deploy.

## Prerequisites

1. **You must be on the `main` branch.** Before proceeding, verify the current branch:
   ```
   git branch --show-current
   ```
   If not on `main`, abort and ask the user to switch to `main` first.

2. **Working directory must be clean.** Check with `git status`.

## Step 1: Check for Dependabot PRs

List all open dependabot PRs:
```bash
gh pr list --author app/dependabot
```

If no PRs exist, inform the user and stop.

## Blocked Packages (DO NOT merge)

These packages have major version bumps that are **incompatible** — skip any PRs for them:

- **Microsoft.ApplicationInsights.WorkerService >=3.0.0** (NuGet) — v3 removes `ITelemetryInitializer`, crashes the Functions worker

These are also configured as `ignore` rules in `.github/dependabot.yml`, but if a PR slips through, do not merge it.

## Step 2: Fetch and Merge Locally

1. Fetch all remote branches:
   ```bash
   git fetch --all
   ```

2. Merge each dependabot branch. For each PR branch:
   ```bash
   git merge origin/<branch-name> --no-edit
   ```

3. **If there are conflicts with lock files** (pnpm-lock.yaml):
   - **Do NOT try to resolve lock file conflicts manually**
   - Accept the current version and mark resolved:
     ```bash
     git checkout --ours pnpm-lock.yaml
     git add pnpm-lock.yaml
     ```
   - Complete the merge commit:
     ```bash
     git commit --no-edit
     ```
   - The lock file will be regenerated in Step 4

## Step 3: Fix @types/node Version

The `@types/node` major version **must match** the Node.js major version in `engines.node` in `patchnotes-web/package.json`. The version is defined in `pnpm-workspace.yaml` under `catalog:`.

1. Check the engine version:
   ```bash
   grep '"node"' patchnotes-web/package.json
   ```

2. Check current @types/node version:
   ```bash
   grep '@types/node' pnpm-workspace.yaml
   ```

3. If @types/node major version doesn't match the engine version (e.g., engine is `>=22` but @types/node is `25.x`), fix it in `pnpm-workspace.yaml`.

## Step 4: Regenerate Lock Files

After all merges, regenerate lock files to ensure consistency:
```bash
pnpm install
```

## Step 5: Regenerate API Types (if orval was updated)

If `orval` was bumped, regenerate the API client types so the version comment matches:
```bash
cd patchnotes-web && pnpm generate:api && cd ..
```

## Step 6: Build and Validate

### Frontend (patchnotes-web)
```bash
cd patchnotes-web && npm run lint && npm run build && npm run test:run && cd ..
```

### Backend (.NET)
```bash
dotnet build PatchNotes.slnx
dotnet test PatchNotes.slnx
```

If any checks fail, fix the issues before proceeding.

## Step 7: Commit and Push

`main` is protected, so push to a branch and create a PR.

1. Stage all changes:
   ```bash
   git add pnpm-lock.yaml
   git add pnpm-workspace.yaml  # if @types/node was fixed
   ```

2. Create a branch, commit, and push:
   ```bash
   git checkout -b chore/merge-dependabot-updates
   git commit -m "chore(deps): merge dependabot updates and regenerate lock file"
   git push -u origin chore/merge-dependabot-updates
   ```

3. Create a PR:
   ```bash
   gh pr create --title "chore(deps): merge dependabot updates" --body "Merges all pending dependabot PRs and regenerates pnpm lock file."
   ```

## Step 8: Monitor CI/Deploy Actions

After pushing, monitor the GitHub Actions workflows:

```bash
# Wait a few seconds for workflows to start
sleep 5

# List recent workflow runs
gh run list --limit 10

# Watch for completion
gh run list --limit 5
```

Report the final status of all triggered workflows to the user.

If any workflow fails, investigate using:
```bash
gh run view <run-id> --log-failed
```
