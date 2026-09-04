# Environment Variables

This document lists all environment variables needed for each service in the ai-patch-notes project.

## PatchNotes.Api (ASP.NET Core Backend)

### Stytch Authentication

| Config Key             | Env Variable            | Required | Description                                    |
| ---------------------- | ----------------------- | -------- | ---------------------------------------------- |
| `Stytch:ProjectId`     | `Stytch__ProjectId`     | Yes      | Stytch project ID for authentication           |
| `Stytch:Secret`        | `Stytch__Secret`        | Yes      | Stytch project secret                          |
| `Stytch:WebhookSecret` | `Stytch__WebhookSecret` | Yes      | Secret for verifying Stytch webhook signatures |

All three Stytch variables are validated at startup; the application will throw if any are missing.

### AI / LLM

| Config Key   | Env Variable  | Required | Default                     | Description                                 |
| ------------ | ------------- | -------- | --------------------------- | ------------------------------------------- |
| `AI:ApiKey`  | `AI__ApiKey`  | Yes      | -                           | API key for the AI/LLM provider             |
| `AI:BaseUrl` | `AI__BaseUrl` | No       | `https://api.openai.com/v1` | Base URL for the AI API                     |
| `AI:Model`   | `AI__Model`   | No       | `gpt-4o-mini`               | Model name to use for patch note generation |

### Stripe

| Config Key             | Env Variable            | Required | Description                                                         |
| ---------------------- | ----------------------- | -------- | ------------------------------------------------------------------- |
| `Stripe:SecretKey`     | `Stripe__SecretKey`     | Yes      | Stripe secret API key (`sk_live_...` or `sk_test_...`)              |
| `Stripe:WebhookSecret` | `Stripe__WebhookSecret` | Yes      | Stripe webhook signing secret (`whsec_...`)                         |
| `Stripe:PriceId`       | `Stripe__PriceId`       | No       | Overrides the default PatchNotes Pro price ID from appsettings.json |

The secret key and webhook secret are configured on Azure App Service. The price ID defaults to the PatchNotes Pro annual plan in `appsettings.json`.

### GitHub

| Config Key     | Env Variable    | Required | Description                                            |
| -------------- | --------------- | -------- | ------------------------------------------------------ |
| `GitHub:Token` | `GitHub__Token` | Yes      | GitHub personal access token for fetching release data |

### Database

| Config Key                     | Env Variable                    | Required | Default                              | Description                                                                            |
| ------------------------------ | ------------------------------- | -------- | ------------------------------------ | -------------------------------------------------------------------------------------- |
| `ConnectionStrings:PatchNotes` | `ConnectionStrings__PatchNotes` | No       | `Data Source=patchnotes.db` (SQLite) | Database connection string. Supports SQLite (file path ending in `.db`) and SQL Server |

When no connection string is provided, the app defaults to a local SQLite database. For production, supply a SQL Server connection string.

## patchnotes-web (Vite/React Frontend)

Location: `patchnotes-web/.env.local` (or `.env.development`)

| Variable                   | Required | Default | Description                                                                                                                                                                           |
| -------------------------- | -------- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `VITE_STYTCH_PUBLIC_TOKEN` | Yes      | -       | Stytch public token for client-side authentication                                                                                                                                    |
| `VITE_API_URL`             | Yes      | `/api`  | Base URL for the PatchNotes API. Set as a GitHub Actions variable (`VITE_API_URL`) for production builds. Local devbox: `http://notes-api.devbox.home.arpa/api` (set in `.env.local`) |

## Local Development (.envrc)

The project root `.envrc` loads secrets from `~/.secrets/patchnotes/.env.local` via `dotenv_if_exists`. Place your local secrets in that file to keep them out of the repository.

Example `~/.secrets/patchnotes/.env.local`:

```env
Stytch__ProjectId=project-test-...
Stytch__Secret=secret-test-...
Stytch__WebhookSecret=whsec_...
Stripe__SecretKey=sk_test_...
Stripe__WebhookSecret=whsec_...
GitHub__Token=ghp_...
AI__ApiKey=sk-...
ConnectionStrings__PatchNotes=Data Source=patchnotes.db
```
