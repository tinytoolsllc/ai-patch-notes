# patchnotes-email

Azure Functions project for email delivery in the PatchNotes application.

## Overview

A TypeScript Azure Functions app that handles email notifications via [Resend](https://resend.com/). Provides welcome emails and weekly digest emails.

## Functions

| Function      | Trigger   | Description                             |
| ------------- | --------- | --------------------------------------- |
| `sendWelcome` | HTTP POST | Sends welcome email to new users        |
| `sendDigest`  | Timer     | Sends weekly digest of release activity |

## Running

```bash
pnpm install
pnpm start
```

This builds the TypeScript and starts the Azure Functions runtime.

## Scripts

| Command      | Description                           |
| ------------ | ------------------------------------- |
| `pnpm build` | Compile TypeScript                    |
| `pnpm watch` | Watch mode compilation                |
| `pnpm clean` | Remove dist/                          |
| `pnpm start` | Clean, build, and start function host |

## Configuration

Create a `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "node",
    "RESEND_API_KEY": "<your-resend-api-key>"
  }
}
```

## Directory Structure

```
patchnotes-email/
├── src/
│   ├── functions/
│   │   ├── sendWelcome.ts    # Welcome email (HTTP trigger)
│   │   └── sendDigest.ts     # Weekly digest (timer trigger)
│   └── lib/
│       └── resend.ts         # Resend client setup
├── host.json                 # Function host configuration
├── package.json
└── tsconfig.json
```

## Database Schema (Prisma)

This project uses [Prisma](https://www.prisma.io/) for database access. **EF Core owns all migrations** — the `PatchNotes.Data` project is the source of truth for the database schema. Prisma is introspection-only here.

**Important:** Never run `prisma migrate` or `prisma db push` in this project. Prisma only reads the schema via `prisma db pull`.

### After changing an EF Core migration

When you add or modify an EF Core migration in `PatchNotes.Data`, you must also update the Prisma schema:

```bash
cd patchnotes-email
pnpm db:pull      # Introspect the database and update prisma/schema.prisma
pnpm db:generate  # Regenerate the Prisma client from the updated schema
```

Commit both the EF Core migration and the updated Prisma schema together.

### CI enforcement

CI runs a `schema-drift` job that:

1. Spins up a SQL Server instance
2. Applies all EF Core migrations
3. Runs `prisma db pull` against that database
4. Fails if `prisma/schema.prisma` differs from what's committed

This prevents the email function from silently breaking due to schema drift.

## Dependencies

- **@azure/functions** - Azure Functions SDK
- **resend** - Email delivery API
- **prisma** / **@prisma/client** - Database access (introspection-only)
