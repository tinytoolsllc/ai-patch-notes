import { useState, useCallback, useMemo } from "react";
import { Link } from "@tanstack/react-router";
import { AppHeader, Container } from "../components/ui";
import { usePackageByOwnerRepo } from "../api/hooks";
import type { PackageDetailGroupDto, PackageDetailResponseDto } from "../api/generated/model";
import { detectPrereleaseType } from "../utils/dateFormat";
import { SummaryCard, type SummaryGroup } from "../components/releases";
import { HeroCard } from "../components/landing/HeroCard";

interface PackageDetailByRepoProps {
  owner: string;
  repo: string;
}

function buildSummaryGroup(
  group: PackageDetailGroupDto,
  pkg: {
    id: string;
    githubOwner: string;
    githubRepo: string;
    npmName?: string | null;
    name: string;
  },
): SummaryGroup {
  const displayName = pkg.npmName ?? `${pkg.githubOwner}/${pkg.githubRepo}`;
  const hasSummary = !!group.summary;
  const releaseCount = group.releaseCount ?? 0;

  let displaySummary = group.summary ?? "";
  if (!displaySummary) {
    const titles = group.releases
      .slice(0, 3)
      .map((r) => r.title || r.tag)
      .join(", ");
    const extra = releaseCount > 3 ? ` and ${releaseCount - 3} more` : "";
    displaySummary = `${releaseCount} release${releaseCount !== 1 ? "s" : ""}: ${titles}${extra}.`;
  }

  return {
    id: `${pkg.githubOwner}-${pkg.githubRepo}-${group.majorVersion}-${group.isPrerelease}`,
    packageId: pkg.id,
    displayName,
    githubOwner: pkg.githubOwner,
    githubRepo: pkg.githubRepo,
    versionRange: group.versionRange,
    isPrerelease: group.isPrerelease,
    prereleaseType: group.isPrerelease ? detectPrereleaseType(group.releases) : undefined,
    displaySummary,
    hasSummary,
    releaseCount,
    lastUpdated: group.lastUpdated ?? "",
    releases: group.releases,
  };
}

function PageHeader({ owner, repo }: { owner: string; repo: string }) {
  return (
    <AppHeader
      breadcrumbs={
        <>
          <Link
            to="/packages/$owner"
            params={{ owner }}
            className="hover:text-text-primary transition-colors"
          >
            {owner}
          </Link>
          <span className="text-text-tertiary">/</span>
          <span className="text-text-primary">{repo}</span>
        </>
      }
    />
  );
}

export function PackageDetailByRepo({ owner, repo }: PackageDetailByRepoProps) {
  const isSyncing = (d: PackageDetailResponseDto | undefined) =>
    d != null && d.groups?.length === 0 && !d.package?.lastFetchedAt;
  const { data, isLoading, error } = usePackageByOwnerRepo(owner, repo, {
    refetchInterval: (query) =>
      isSyncing(query.state.data as PackageDetailResponseDto | undefined) ? 30_000 : false,
  });
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(new Set());

  const toggleExpanded = useCallback((groupId: string) => {
    setExpandedGroups((prev) => {
      const next = new Set(prev);
      if (next.has(groupId)) {
        next.delete(groupId);
      } else {
        next.add(groupId);
      }
      return next;
    });
  }, []);

  const summaryGroups = useMemo(() => {
    if (!data) return [];
    return data.groups.map((g) => buildSummaryGroup(g, data.package));
  }, [data]);

  if (isLoading) {
    return (
      <div className="min-h-screen bg-surface-secondary">
        <PageHeader owner={owner} repo={repo} />
        <main className="py-8">
          <Container>
            <p className="text-text-secondary">Loading package details...</p>
          </Container>
        </main>
      </div>
    );
  }

  if (error || !data) {
    return (
      <div className="min-h-screen bg-surface-secondary">
        <PageHeader owner={owner} repo={repo} />
        <main className="py-8">
          <Container>
            <p className="text-text-secondary">The requested package could not be found.</p>
          </Container>
        </main>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-surface-secondary">
      <PageHeader owner={owner} repo={repo} />

      <main className="py-8">
        <Container>
          <HeroCard />

          {/* Version Groups */}
          <div className="space-y-4">
            {summaryGroups.map((group) => (
              <SummaryCard
                key={group.id}
                group={group}
                isExpanded={expandedGroups.has(group.id)}
                onToggle={toggleExpanded}
              />
            ))}
          </div>

          {data.groups.length === 0 &&
            (!data.package.lastFetchedAt ? (
              <div className="text-center py-12">
                <div className="inline-block h-6 w-6 animate-spin rounded-full border-2 border-current border-t-transparent text-brand-primary" />
                <p className="text-text-secondary mt-3">
                  Syncing releases for this package&hellip;
                </p>
              </div>
            ) : (
              <p className="text-text-secondary text-center py-8">
                No releases found for this package.
              </p>
            ))}
        </Container>
      </main>
    </div>
  );
}
