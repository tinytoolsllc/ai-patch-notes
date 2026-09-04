import { Badge } from "../ui";

interface VersionBadgeProps {
  version: string;
  className?: string;
}

type ReleaseType = "major" | "minor" | "patch" | "prerelease";

function parseReleaseType(version: string): ReleaseType {
  // Handle prerelease versions (alpha, beta, rc, etc.)
  if (/-(alpha|beta|rc|next|canary|dev|preview)/i.test(version)) {
    return "prerelease";
  }

  // Try to extract semver parts
  const match = version.match(/^v?(\d+)\.(\d+)\.(\d+)/);
  if (!match) {
    return "patch"; // Default for non-semver
  }

  const [, major, minor, patch] = match;

  // Heuristic: if patch > 0, it's likely a patch release
  // if minor > 0 and patch === 0, it's likely a minor release
  // if major > 0 and minor === 0 and patch === 0, it's likely a major release
  if (patch !== "0") {
    return "patch";
  }
  if (minor !== "0") {
    return "minor";
  }
  if (major !== "0") {
    return "major";
  }

  return "patch";
}

export function VersionBadge({ version, className }: VersionBadgeProps) {
  const releaseType = parseReleaseType(version);

  return (
    <Badge variant={releaseType} className={`font-mono ${className || ""}`}>
      {version}
    </Badge>
  );
}
