# Upgrade Summarizer Model: gemma3:27b to gemma4:31b

> Validate and switch the release-note summarizer from gemma3:27b to gemma4:31b on Ollama Cloud.

## Motivation

The project currently uses `gemma3:27b` for AI-generated release summaries via the OpenAI-compatible API at `https://ollama.com/v1/`. The newer `gemma4:31b` offers:

- **Native structured JSON output** (not just Ollama grammar enforcement)
- **256K context window** (vs 128K)
- Better instruction following

The LogWatcher feature spec already targets gemma4:31b. Upgrading the summarizer keeps both features on the same model, simplifying configuration and making Ollama Cloud usage more predictable.

## Scope

- Validate gemma4:31b produces summaries at least as good as gemma3:27b
- Switch the default model in configuration
- No changes to the prompt, `AiClient`, or `IAiClient` interface

## Risk

Low. The change is a single config value (`AI:Model`). The prompt, API endpoint, and response format are identical. If quality regresses, reverting is a one-line config change.

## Validation Plan

### Step 1: Collect baseline samples from gemma3:27b

Run the summarizer against a fixed set of release inputs and save the outputs. Use packages with varied changelog styles:

| Package         | Release(s)                            | Why this sample                                     |
| --------------- | ------------------------------------- | --------------------------------------------------- |
| Vite            | A major release (e.g., v8.0.0)        | Long changelog, breaking changes, multiple sections |
| React           | A patch release (e.g., v19.0.1)       | Short changelog, mostly fixes                       |
| TanStack Router | A minor release with several features | Mid-length, feature-focused                         |
| ESLint          | A major release with migration notes  | Breaking changes + upgrade steps                    |
| Tailwind CSS    | A prerelease/beta                     | Prerelease-specific language, experimental features |

For each sample, record:

- The exact input (package name + release tag + body)
- The gemma3:27b output
- Timestamp and any token usage reported

**How to run:** Use the existing sync CLI or call the API's summary regeneration endpoint against a local dev database seeded with these packages.

### Step 2: Generate gemma4:31b outputs for the same inputs

Change `AI:Model` to `gemma4:31b` in local config and re-run the same inputs. Save outputs side by side.

### Step 3: Compare on quality criteria

For each sample pair, evaluate:

| Criterion                        | What to check                                                                                                                                      |
| -------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Format compliance**            | Does the output follow the expected section structure (TL;DR, Breaking, New, Fixes Worth Knowing, Before You Upgrade)? Are empty sections omitted? |
| **Accuracy**                     | Are breaking changes correctly identified? Are features attributed correctly? No hallucinated items?                                               |
| **Conciseness**                  | Is it under 150 words? Is it tighter or more verbose than gemma3?                                                                                  |
| **Tone**                         | Direct, developer-facing, no filler? Matches the "senior developer writing for their team" voice?                                                  |
| **Parenthetical clarifications** | Are unfamiliar concepts given 3-5 word clarifications as instructed?                                                                               |
| **Multi-release consolidation**  | For grouped releases, does it produce a single coherent summary rather than listing each release separately?                                       |
| **Edge cases**                   | How does it handle changelogs that are mostly links, very short, or contain only a title with no body?                                             |

### Step 4: Spot-check regressions

Pay particular attention to:

- **Empty section handling**: gemma3 had a tendency to output "None" under headings despite the prompt saying not to. The actual prompt has a `CRITICAL` instruction about this. Check if gemma4 follows it better or worse.
- **Breaking change identification**: Does gemma4 correctly identify breaking changes from context when the changelog doesn't explicitly label them?
- **Word count**: Does gemma4 tend to be more verbose? If so, the 150-word limit may need reinforcement.
- **Markdown formatting**: Clean heading levels, bullet points, code references?

### Step 5: Decision

- If gemma4:31b matches or improves on gemma3:27b across all samples: **ship it**
- If gemma4:31b regresses on 1-2 criteria but improves on others: evaluate trade-offs, consider minor prompt adjustments
- If gemma4:31b consistently regresses: stay on gemma3:27b for the summarizer, still use gemma4 for LogWatcher

## Rollout

1. Run the validation locally
2. Update `appsettings.json` default: `"Model": "gemma4:31b"`
3. Deploy
4. Monitor the next sync cycle's summary output in App Insights logs for parse errors or anomalies
5. Spot-check a few summaries on the live site after the first hourly sync

## Fallback

Revert `AI:Model` to `gemma3:27b` in config. No code changes needed.
