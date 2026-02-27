# Remaining Work: GitHub Search Migration to SWA API

## 1. Azure Configuration

- Set `GITHUB_PAT` in the Azure Static Web App application settings.
- Confirm `GITHUB_PAT` has enough GitHub API scope for repository search.
- Verify SWA environment has `apiRuntime` Node 20 enabled.

## 2. Deployment

- Merge and deploy branch `feat/swa-github-search`.
- Confirm SWA deployment includes both:
  - frontend artifact (`patchnotes-web/dist`)
  - managed API (`patchnotes-web/api`)

## 3. Runtime Validation (Production)

- Validate `GET /api/github-search?q=react` returns 200 with mapped fields:
  - `owner`
  - `repo`
  - `description`
  - `starCount`
- Validate validation behavior:
  - `q` missing -> 400
  - `q` shorter than 2 chars -> 400
- Validate frontend watchlist search works end-to-end with real auth session.

## 4. Operational Checks

- Confirm cache behavior works on warm instances (`X-Cache: HIT/MISS` headers).
- Check GitHub API error path returns 502 and is logged.
- Watch for GitHub rate-limit pressure after rollout.

## 5. Documentation Follow-up

- Update any runbooks/playbooks that still mention `/api/github/search`.
- Add local dev note for managed API testing via SWA CLI if needed.

