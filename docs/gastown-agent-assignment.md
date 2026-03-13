# Gastown Agent Assignment

Reference for configuring which AI models/runtimes are used by different roles and crew members in a Gas Town deployment.

Source: [gastown v0.12.0](https://github.com/steveyegge/gastown/releases/tag/v0.12.0), specifically commit [43c2253c](https://github.com/steveyegge/gastown/commit/43c2253c1d6cb3ca0a42c37db5e25285f8c0f159) for `crew_agents`.

## Resolution Order

When Gas Town determines which agent to use for a worker, it checks these sources in order (highest priority first):

1. `--agent` CLI flag (per-invocation override)
2. Rig-level `WorkerAgents[name]` (per-rig, per-worker override)
3. Town-level `CrewAgents[name]` (town-wide, per-crew-member override)
4. Town-level `role_agents[role]` (town-wide, per-role default)
5. Built-in default agent

## role_agents

Maps infrastructure roles to agent aliases. Use this to control which model handles each role across the entire town.

```json
{
  "role_agents": {
    "mayor": "opus-46",
    "witness": "haiku",
    "refinery": "sonnet-46",
    "deacon": "haiku",
    "polecat": "opus-46-capped",
    "dog": "haiku"
  }
}
```

Typical mapping strategy:
- **mayor, polecat**: High-capability models (Opus) since they do complex reasoning and code generation
- **witness, deacon, dog**: Cheaper/faster models (Haiku, Sonnet) since they handle coordination, health checks, and plugin dispatch
- **refinery**: Mid-tier model (Sonnet) for merge decisions and code review

## crew_agents

Maps named crew members to agent aliases. Use this to assign specific models to individual contributors without modifying per-rig config.

```json
{
  "crew_agents": {
    "bob": "codex",
    "alice": "claude",
    "carol": "kimi-k2.5"
  }
}
```

This is useful when crew members have different needs — e.g., one works on a codebase that benefits from a specific model's strengths, or you want to A/B test models across crew members.

`crew_agents` overrides `role_agents` but is overridden by rig-level `WorkerAgents` and the `--agent` CLI flag.

## Agent Aliases

Both maps reference agent aliases, not raw model IDs. Aliases are defined in the `agents` section of town settings, mapping a short name to a full model/runtime configuration:

```json
{
  "agents": {
    "opus-46": { ... },
    "opus-46-capped": { ... },
    "haiku": { ... },
    "sonnet-46": { ... },
    "codex": { ... },
    "kimi-k2.5": { ... },
    "sonnet-via-opencode": { ... }
  }
}
```

See `docs/examples/town-settings.example.json` in the [gastown repo](https://github.com/steveyegge/gastown) for the full alias definitions.

## Per-Rig Overrides

For project-specific overrides, set `WorkerAgents` in the rig config (not town settings). This takes priority over both `crew_agents` and `role_agents`:

```json
{
  "worker_agents": {
    "alice": "opus-46"
  }
}
```

This is useful when a specific rig needs a more capable model due to codebase complexity, regardless of the town-wide crew assignment.
