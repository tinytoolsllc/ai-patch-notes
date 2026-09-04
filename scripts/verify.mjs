#!/usr/bin/env node

/**
 * Runs locally what CI runs for the JavaScript packages, in one command.
 *
 * Usage: pnpm verify
 *
 * Why this exists: CI splits its checks across several jobs and steps, and it is
 * easy to run a subset locally, see green, and still fail CI. Three separate
 * failures were found this way in a single afternoon -- each caught by a
 * different check that had not been run locally:
 *
 *   tsc      a dependency's .d.ts files did not typecheck
 *   orval    an npm-alias spec in the catalog is not valid semver
 *   oxfmt    a formatter upgrade reformatted two existing files
 *
 * Every step below mirrors a step in .github/workflows/ci.yml, except the two
 * marked NOT-IN-CI, which covers a real gap: CI's "Email Function (Node.js)" job
 * runs only `pnpm build`, so the email package's 47 tests never run in CI at
 * all. They run here. (Its lint is covered by the repo-wide oxlint step.)
 *
 * NOT covered: the .NET side (backend build/test, EF model-change check,
 * OpenAPI spec drift) and the Prisma schema drift job. Run those with dotnet
 * directly if you are touching the API or the data model.
 */

import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** @type {{name: string, cmd: string, args: string[], cwd: string, note?: string}[]} */
const steps = [
  {
    name: "web: generate:api",
    cmd: "pnpm",
    args: ["--filter", "patchnotes-web", "generate:api"],
    cwd: repoRoot,
  },
  {
    name: "web: generated API drift",
    cmd: "git",
    args: ["diff", "--exit-code", "--", "patchnotes-web/src/api/generated/"],
    cwd: repoRoot,
  },
  {
    // oxlint is configured at the repo root and covers both packages, so one
    // root run replaces what used to be two eslint runs. Type-aware linting
    // (tsgolint) is switched on via `options.typeAware` in .oxlintrc.json
    // rather than a CLI flag, so it cannot be dropped by editing a script --
    // which matters, because a non-type-aware run reports nothing here.
    name: "repo: lint (oxlint)",
    cmd: "pnpm",
    args: ["lint"],
    cwd: repoRoot,
  },
  {
    // oxfmt is configured at the repo root and covers YAML, Markdown and JSON
    // as well as the TypeScript packages, so this is not package-scoped.
    name: "repo: format:check",
    cmd: "pnpm",
    args: ["format:check"],
    cwd: repoRoot,
  },
  {
    name: "web: test",
    cmd: "pnpm",
    args: ["--filter", "patchnotes-web", "test:run"],
    cwd: repoRoot,
  },
  {
    name: "web: build",
    cmd: "pnpm",
    args: ["--filter", "patchnotes-web", "build"],
    cwd: repoRoot,
  },
  {
    name: "email: build",
    cmd: "pnpm",
    args: ["--filter", "patchnotes-email", "build"],
    cwd: repoRoot,
  },
  {
    name: "email: test",
    cmd: "pnpm",
    args: ["--filter", "patchnotes-email", "test"],
    cwd: repoRoot,
    note: "NOT-IN-CI",
  },
];

const results = [];

for (const step of steps) {
  const label = step.note ? `${step.name}  (${step.note})` : step.name;
  console.log(`\n[1m[36m▶ ${label}[0m`);

  // Run through a shell so `pnpm` resolves on every platform (it is a .cmd
  // shim on Windows). The command is passed as one string rather than an args
  // array -- that combination is what Node DEP0190 warns about, and every
  // command here is a fixed literal with no interpolated input.
  const { status } = spawnSync([step.cmd, ...step.args].join(" "), {
    cwd: step.cwd,
    stdio: "inherit",
    shell: true,
  });

  const ok = status === 0;
  results.push({ label, ok });

  if (!ok && step.name === "web: generated API drift") {
    console.error("\n  Generated API types are stale. Commit the output of 'pnpm generate:api'.");
  }
}

const failed = results.filter((r) => !r.ok);

console.log("\n" + "─".repeat(60));
for (const r of results) {
  console.log(`  ${r.ok ? "[32mPASS[0m" : "[31mFAIL[0m"}  ${r.label}`);
}
console.log("─".repeat(60));

if (failed.length > 0) {
  console.error(`\n[31m${failed.length} of ${results.length} checks failed.[0m`);
  process.exit(1);
}

console.log(`\n[32mAll ${results.length} checks passed.[0m`);
