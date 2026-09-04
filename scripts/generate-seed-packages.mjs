#!/usr/bin/env node

/**
 * Generates PatchNotes.Data/SeedData/packages.json from docs/sample-data/seed-libraries.json.
 *
 * Merges both the "js" and "react" library sets, deduplicates by github_full_name,
 * and preserves any existing entries in packages.json that aren't in the library list.
 *
 * Usage:
 *   node scripts/generate-seed-packages.mjs
 */

import { readFileSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const rootDir = join(__dirname, "..");

const librariesPath = join(rootDir, "docs/sample-data/seed-libraries.json");
const seedPath = join(rootDir, "PatchNotes.Data/SeedData/packages.json");

// Read source data
const libraries = JSON.parse(readFileSync(librariesPath, "utf-8"));

// Read existing seed data to preserve entries with releases
let existingSeed = [];
try {
  existingSeed = JSON.parse(readFileSync(seedPath, "utf-8"));
} catch {
  // No existing seed file, start fresh
}

// Build a map of existing packages by githubOwner/githubRepo for merging
const existingByRepo = new Map();
for (const pkg of existingSeed) {
  const key = `${pkg.githubOwner}/${pkg.githubRepo}`;
  existingByRepo.set(key, pkg);
}

// Merge js + react sets, deduplicate by github_full_name
const seen = new Set();
const allLibraries = Object.values(libraries).flat();
const dedupedLibraries = [];

for (const lib of allLibraries) {
  if (!lib.github_full_name || seen.has(lib.github_full_name)) continue;
  seen.add(lib.github_full_name);
  dedupedLibraries.push(lib);
}

// Transform each library into the seed format
const seedPackages = dedupedLibraries.map((lib) => {
  const [githubOwner, githubRepo] = lib.github_full_name.split("/");
  const repoKey = `${githubOwner}/${githubRepo}`;

  // If this package already exists in the seed file, preserve its releases
  const existing = existingByRepo.get(repoKey);
  if (existing) {
    // Update metadata but keep releases
    existingByRepo.delete(repoKey);
    return {
      name: lib.nameClean,
      url: lib.github_url,
      npmName: lib.npm || existing.npmName || null,
      githubOwner,
      githubRepo,
      releases: existing.releases || [],
    };
  }

  return {
    name: lib.nameClean,
    url: lib.github_url,
    npmName: lib.npm || null,
    githubOwner,
    githubRepo,
    releases: [],
  };
});

// Append any existing seed packages that weren't in the library list
for (const pkg of existingByRepo.values()) {
  seedPackages.push(pkg);
}

// Sort alphabetically by name for readability
seedPackages.sort((a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" }));

writeFileSync(seedPath, JSON.stringify(seedPackages, null, 2) + "\n");

console.log(`Wrote ${seedPackages.length} packages to PatchNotes.Data/SeedData/packages.json`);
console.log(
  `  - ${dedupedLibraries.length} from seed-libraries.json (${allLibraries.length} total, ${allLibraries.length - dedupedLibraries.length} duplicates removed)`,
);
console.log(`  - ${existingByRepo.size} preserved from existing seed data`);
