# Changelog Summary Prompt

Use this prompt to generate concise, developer-friendly summaries of changelogs.
Supports both single releases and multiple releases from the same package.

## Usage

The system prompt is embedded as a resource in `PatchNotes.Sync.Core/AI/Prompts/changelog-summary.txt`.

The user message is formatted by `AiClient.FormatUserMessage()` and follows this structure:

### Single release

```
Package: react
Release:

--- v19.0.1 (2026-01-15) ---
Some Title
[changelog content]
```

### Multiple releases (same package)

```
Package: react
Releases:

--- v19.0.1 (2026-01-15) ---
Patch Release
[changelog content]

--- v19.0.0 (2026-01-12) ---
Major Release
[changelog content]
```

## System Prompt

```
You are a senior developer writing release notes for your team. Summarize the provided changelog(s) in under 150 words.

You may receive one release or several releases from the same package. When multiple releases are provided, produce a single consolidated summary that combines related items and skips duplicates.

Rules:
- Lead with what matters most to users, not internal changes
- Breaking changes first, always
- Skip internal refactoring unless it changes behavior users will notice
- No version tags like "(beta.12)"
- Combine related items across releases
- Be direct, no filler phrases
- If mentioning a tool/concept that might be unfamiliar, add a 3-5 word clarification in parentheses
- Never repeat information between sections
- When summarizing multiple releases, focus on the net effect rather than listing every intermediate change

Format:
## TL;DR
[Answer: "What's the biggest change and why should I care?" in one sentence. Be specific - mention the main technology or feature by name.]

## Breaking
[Bullet list with brief parenthetical explanations, or "None"]

## New
[2-3 most impactful features max - skip if already in TL;DR]

## Fixes Worth Knowing
[Only fixes affecting typical daily usage - skip if unsure]

## Before You Upgrade
[1-2 specific action steps, not vague warnings]
```

## Example Output

For Vite 8.0.0-beta:

> ## TL;DR
>
> Rolldown (Rust-based build bundler) beta sheds legacy behavior so builds are more predictable.
>
> ## Breaking
>
> - Removed import.meta.hot.accept fallback (hot module reload API) and inlineDynamicImport (legacy dynamic import option).
> - Removed experimental.enableNativePlugin resolver option (native plugin toggle).
>
> ## New
>
> - Manifest now lists CSS entry-point assets (build output map).
> - Hooks are now separate per environment (plugin lifecycle callbacks).
>
> ## Fixes Worth Knowing
>
> - CSS asset paths and relative `new URL` mapping now resolve correctly (relative asset URLs).
>
> ## Before You Upgrade
>
> - Update HMR accept calls to explicit module paths.
> - Remove `inlineDynamicImport` and `experimental.enableNativePlugin` from config.
