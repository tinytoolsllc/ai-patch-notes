import { useState, useCallback, useMemo } from 'react'
import { Link } from '@tanstack/react-router'
import { AppHeader, Container } from '../components/ui'
import { usePackageByOwnerRepo } from '../api/hooks'
import type { PackageDetailGroupDto } from '../api/generated/model'
import { detectPrereleaseType } from '../utils/dateFormat'
import { SummaryCard, type SummaryGroup } from '../components/releases'
import { HeroCard } from '../components/landing/HeroCard'

interface PackageDetailByRepoProps {
  owner: string
  repo: string
}

function buildSummaryGroup(
  group: PackageDetailGroupDto,
  pkg: {
    githubOwner: string
    githubRepo: string
    npmName?: string | null
    name: string
  }
): SummaryGroup {
  const displayName = pkg.npmName ?? `${pkg.githubOwner}/${pkg.githubRepo}`
  const hasSummary = !!group.summary
  const releaseCount = group.releaseCount ?? 0

  let displaySummary = group.summary ?? ''
  if (!displaySummary) {
    const titles = group.releases
      .slice(0, 3)
      .map((r) => r.title || r.tag)
      .join(', ')
    const extra = releaseCount > 3 ? ` and ${releaseCount - 3} more` : ''
    displaySummary = `${releaseCount} release${releaseCount !== 1 ? 's' : ''}: ${titles}${extra}.`
  }

  return {
    id: `${pkg.githubOwner}-${pkg.githubRepo}-${group.majorVersion}-${group.isPrerelease}`,
    displayName,
    githubOwner: pkg.githubOwner,
    githubRepo: pkg.githubRepo,
    versionRange: group.versionRange,
    isPrerelease: group.isPrerelease,
    prereleaseType: group.isPrerelease
      ? detectPrereleaseType(group.releases)
      : undefined,
    displaySummary,
    hasSummary,
    releaseCount,
    lastUpdated: group.lastUpdated ?? '',
    releases: group.releases,
  }
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
  )
}

export function PackageDetailByRepo({ owner, repo }: PackageDetailByRepoProps) {
  const { data, isLoading, error } = usePackageByOwnerRepo(owner, repo)
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(new Set())

  const toggleExpanded = useCallback((groupId: string) => {
    setExpandedGroups((prev) => {
      const next = new Set(prev)
      if (next.has(groupId)) {
        next.delete(groupId)
      } else {
        next.add(groupId)
      }
      return next
    })
  }, [])

  const summaryGroups = useMemo(() => {
    if (!data) return []
    return data.groups.map((g) => buildSummaryGroup(g, data.package))
  }, [data])

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
    )
  }

  if (error || !data) {
    return (
      <div className="min-h-screen bg-surface-secondary">
        <PageHeader owner={owner} repo={repo} />
        <main className="py-8">
          <Container>
            <p className="text-text-secondary">
              The requested package could not be found.
            </p>
          </Container>
        </main>
      </div>
    )
  }

  const pkg = data.package
  const githubUrl = `https://github.com/${pkg.githubOwner}/${pkg.githubRepo}`

  return (
    <div className="min-h-screen bg-surface-secondary">
      <PageHeader owner={owner} repo={repo} />

      <main className="py-8">
        <Container>
          <HeroCard />

          {/* Package Info */}
          <div className="mb-6">
            <div className="flex items-center gap-3 mb-2">
              <div className="w-12 h-12 rounded-lg bg-brand-100 dark:bg-brand-900/30 flex items-center justify-center font-semibold text-xl text-brand-600 dark:text-brand-400">
                {(pkg.npmName ?? pkg.name).charAt(0).toUpperCase()}
              </div>
              <div>
                <h1 className="text-2xl font-bold text-text-primary">
                  {pkg.npmName ?? pkg.name}
                </h1>
                <p className="text-sm text-text-secondary">
                  {pkg.githubOwner}/{pkg.githubRepo}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-4 text-sm mt-3">
              <a
                href={githubUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="text-text-secondary hover:text-brand-600 transition-colors"
              >
                View on GitHub
              </a>
              {pkg.npmName && (
                <a
                  href={`https://www.npmjs.com/package/${pkg.npmName}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-text-secondary hover:text-brand-600 transition-colors"
                >
                  View on npm
                </a>
              )}
            </div>
          </div>

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

          {data.groups.length === 0 && (
            <p className="text-text-secondary text-center py-8">
              No releases found for this package.
            </p>
          )}
        </Container>
      </main>
    </div>
  )
}
