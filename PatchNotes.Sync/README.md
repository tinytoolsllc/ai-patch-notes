# PatchNotes.Sync

Console application for syncing GitHub releases and generating AI summaries for tracked packages.

## Overview

This project fetches release information from GitHub for all tracked npm packages, stores them in the database, and generates AI-powered release summaries. It uses a **producer-consumer pipeline** (`SyncPipeline`) — as soon as a package finishes syncing, its summaries start generating while the next package syncs.

## Usage

### Full Sync Pipeline

```bash
dotnet run
```

Concurrently syncs releases from GitHub and generates AI summaries for all tracked packages.

### CLI Options

| Flag | Description | Example |
|------|-------------|---------|
| `--init` | Seed package catalog from packages.json and sync all | `dotnet run -- --init` |
| `--seed` | Seed database with sample data (local dev) | `dotnet run -- --seed` |
| `-r <url>` | Sync a single GitHub repo | `dotnet run -- -r https://github.com/prettier/prettier` |
| `-s <owner/repo>` | Generate summaries for a specific package | `dotnet run -- -s prettier/prettier` |

### Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success - all packages synced |
| 1 | Partial failure - some packages failed |
| 2 | Fatal error - sync could not complete |

## Components

- **SyncPipeline.cs** - Producer-consumer pipeline using `System.Threading.Channels`
- **SyncService.cs** - Core sync logic for fetching and storing releases from GitHub
- **SummaryGenerationService.cs** - AI summary generation per version group (major version + prerelease flag)
- **VersionGroupingService.cs** - Groups releases by (PackageId, MajorVersion, IsPrerelease)
- **ChangelogResolver.cs** - Fetches external changelog files from GitHub repos

## Configuration

Configuration is loaded from `appsettings.json` and environment variables:

- `GitHub:Token` - GitHub personal access token (increases API rate limit from 60 to 5000 requests/hour)
- `AI:BaseUrl` - AI provider base URL (default: `https://ollama.com/v1/`)
- `AI:ApiKey` - AI provider API key
- `AI:Model` - AI model name (default: `gemma4:31b`)

## Directory Structure

```
PatchNotes.Sync/
├── GitHub/                   # GitHub API client
│   ├── GitHubClient.cs       # GitHub API implementation
│   ├── IGitHubClient.cs      # Client interface
│   ├── GitHubClientOptions.cs
│   ├── GitHubServiceCollectionExtensions.cs
│   ├── RateLimitHelper.cs    # Rate limit handling
│   └── Models/               # GitHub DTOs
├── AI/                       # AI client (OpenAI-compatible)
│   ├── AiClient.cs           # AI client implementation
│   ├── IAiClient.cs          # Client interface
│   ├── AiClientOptions.cs
│   ├── AiServiceCollectionExtensions.cs
│   ├── ReleaseInput.cs       # AI input model
│   ├── Models/               # Chat completion DTOs
│   └── Prompts/              # Embedded prompt templates
├── SyncPipeline.cs           # Producer-consumer pipeline
├── SyncService.cs            # GitHub sync logic
├── SummaryGenerationService.cs # AI summary generation
├── VersionGroupingService.cs # Release version grouping
├── ChangelogResolver.cs      # Changelog file fetching
├── GitHubUrlParser.cs        # GitHub URL parsing
├── Program.cs                # CLI entry point
├── SyncResults.cs            # Result types
└── PipelineResult.cs         # Pipeline result
```

## Dependencies

- **PatchNotes.Data** - Database context and entity models
- **Microsoft.Extensions.Hosting** - Host builder and DI
- **Microsoft.Extensions.Http** - HttpClient factory
