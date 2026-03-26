# investigate: Systematic Root Cause Debugging

Structured debugging methodology that finds root causes before applying fixes. Use when
a bug is reported, a test fails unexpectedly, or something "just stopped working."

**Iron Law: No fixes without root cause investigation first.**

## When to Use

- Bug reports from users or QA
- Test failures you don't immediately understand
- "It was working yesterday" situations
- Production errors or crashes
- Performance regressions

## Phase 1: Gather & Reproduce

Before touching any code, understand the problem:

1. **Collect symptoms** — What exactly is failing? Error messages, stack traces, screenshots, user reports.
2. **Reproduce the issue** — Can you trigger it reliably? What are the exact steps?
3. **Check recent changes** — `git log --oneline -20` and `git diff HEAD~5` — did something change recently?
4. **Narrow the scope** — Is it one endpoint, one page, one function? Or widespread?

If you cannot reproduce after 3 attempts, stop and ask the user for more context.

## Phase 2: Analyze

Match the symptoms against known patterns:

| Pattern | Indicators |
|---------|------------|
| Race condition | Intermittent, timing-dependent, works in debugger |
| Null/undefined propagation | TypeError, "cannot read property of null/undefined" |
| State corruption | Works on first load, fails on subsequent interactions |
| Data mismatch | Works with some data, fails with other data |
| Environment issue | Works locally, fails in CI/staging/prod |
| Dependency change | Worked before package update, lockfile changed |
| Migration issue | DB-related errors after schema change |
| Cache staleness | Works after hard refresh or cache clear |
| Auth/session issue | Works when freshly logged in, fails later |
| Concurrency issue | Works with one user, fails under load |

## Phase 3: Hypothesize & Test

1. **Form a hypothesis** — "I think X is happening because Y"
2. **Design a test** — How can you prove or disprove this? Add targeted logging, write a minimal reproduction, check specific state.
3. **Test the hypothesis** — Run the test. Does it confirm or refute?
4. **If refuted** — Form a new hypothesis. Do NOT fix something that isn't the root cause.
5. **3-strike rule** — If 3 hypotheses fail, stop and escalate. Share what you've tried.

### Sanitize Before Searching

When searching for errors online or in codebase:
- Strip specific values (IDs, paths, timestamps)
- Keep the error structure and type
- Example: `TypeError: Cannot read property 'id' of undefined at UserService.getUser` → search for `TypeError: Cannot read property of undefined UserService`

## Phase 4: Fix

Only after root cause is confirmed:

1. **Fix the root cause, not the symptom** — If a null value crashes downstream, fix where null is introduced, not where it crashes.
2. **Minimal diff** — Change only what's necessary. Don't refactor while fixing.
3. **Write a regression test** — A test that would have caught this bug before the fix, and passes after.
4. **Verify the fix** — Run the full test suite. Manually reproduce the original steps and confirm the bug is gone.
5. **Check blast radius** — Does this fix affect other code paths? Run `git diff --stat` — if >5 files changed, flag it.

## Phase 5: Report

After fixing, write a brief debug report:

```
## Debug Report

**Issue:** [one-line description]
**Root cause:** [what was actually wrong]
**Fix:** [what was changed and why]
**Regression test:** [test file:line that prevents recurrence]
**Blast radius:** [what else might be affected]
**Time spent:** [how long the investigation took]
```

## Important Rules

1. **Never apply unverified fixes.** "Maybe this will work" is not a fix — it's a guess. Verify first.
2. **Read before writing.** Understand the code path before changing it.
3. **One fix at a time.** Don't combine multiple fixes — you won't know which one worked.
4. **Escalate early.** After 3 failed hypotheses, stop. Share findings and ask for help.
5. **Flag large blast radius.** If a fix touches >5 files, pause and discuss with the user.
6. **Don't optimize while debugging.** Fix the bug. Optimization is a separate task.
7. **Check the obvious first.** Typos, wrong variable names, missing imports, incorrect config.
8. **Trust error messages.** Read them carefully. They usually tell you exactly what's wrong.
9. **Git blame is your friend.** When did this code change? Who changed it? What was the commit message?
10. **Environment matters.** Check env vars, config files, database state, API versions.
