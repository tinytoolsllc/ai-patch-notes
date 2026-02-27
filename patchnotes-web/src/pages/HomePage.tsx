import { useState, useMemo, useCallback } from 'react'
import { Link } from '@tanstack/react-router'
import { useStytchUser } from '@stytch/react'
import {
  FlaskConical,
  FlaskConicalOff,
  ArrowDownAZ,
  CalendarArrowDown,
  Group,
} from 'lucide-react'
import { AppHeader, Container, Card, Tooltip } from '../components/ui'
import { detectPrereleaseType, type PrereleaseType } from '../utils/dateFormat'
import {
  SummaryCard,
  PackageIcon,
  type SummaryGroup,
} from '../components/releases'
import { HeroCard } from '../components/landing/HeroCard'
import { useFilterStore } from '../stores/filterStore'
import { useWatchlist, useFeed } from '../api/hooks'
import type { FeedGroupDto } from '../api/hooks'

// ============================================================================
// Types
// ============================================================================

interface VersionGroup extends SummaryGroup {
  prereleaseType?: PrereleaseType
}

// ============================================================================
// Utility Functions
// ============================================================================

function buildDisplayGroups(groups: FeedGroupDto[]): VersionGroup[] {
  return groups.map((g) => {
    const displayName = g.npmName ?? `${g.githubOwner}/${g.githubRepo}`
    const hasSummary = !!g.summary
    // Use AI summary if available, otherwise build a placeholder
    let displaySummary = g.summary ?? ''
    const releaseCount = g.releaseCount ?? 0
    if (!displaySummary) {
      const titles = g.releases
        .slice(0, 3)
        .map((r) => r.title || r.tag)
        .join(', ')
      const extra = releaseCount > 3 ? ` and ${releaseCount - 3} more` : ''
      displaySummary = `${releaseCount} release${releaseCount !== 1 ? 's' : ''} in this version: ${titles}${extra}.`
    }

    return {
      ...g,
      id: `${g.packageId}-${g.majorVersion}-${g.isPrerelease}`,
      displayName,
      releaseCount,
      prereleaseType: g.isPrerelease
        ? detectPrereleaseType(g.releases)
        : undefined,
      displaySummary,
      hasSummary,
    }
  })
}

// ============================================================================
// Components
// ============================================================================

function SkeletonCard() {
  return (
    <Card className="animate-pulse">
      <div className="flex items-start justify-between gap-4 mb-4">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-lg bg-surface-tertiary" />
          <div>
            <div className="h-5 w-32 bg-surface-tertiary rounded mb-2" />
            <div className="h-4 w-20 bg-surface-tertiary rounded" />
          </div>
        </div>
        <div className="h-6 w-16 bg-surface-tertiary rounded-full" />
      </div>
      <div className="space-y-2">
        <div className="h-4 w-full bg-surface-tertiary rounded" />
        <div className="h-4 w-5/6 bg-surface-tertiary rounded" />
        <div className="h-4 w-4/6 bg-surface-tertiary rounded" />
      </div>
      <div className="flex items-center gap-4 mt-4 pt-4 border-t border-border-muted">
        <div className="h-4 w-24 bg-surface-tertiary rounded" />
        <div className="h-4 w-20 bg-surface-tertiary rounded" />
      </div>
    </Card>
  )
}

function FilterButton({
  active,
  onClick,
  tooltip,
  className = '',
  children,
}: {
  active: boolean
  onClick: () => void
  tooltip: string
  className?: string
  children: React.ReactNode
}) {
  return (
    <Tooltip label={tooltip}>
      <button
        onClick={onClick}
        className={`
          flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium transition-all
          ${
            active
              ? 'bg-surface-primary text-text-primary shadow-sm'
              : 'text-text-secondary hover:text-text-primary hover:bg-surface-primary/50'
          }
          ${className}
        `}
      >
        {children}
      </button>
    </Tooltip>
  )
}

// ============================================================================
// Main Component
// ============================================================================

export function HomePage() {
  const {
    showPrerelease,
    sortBy,
    groupByPackage,
    togglePrerelease,
    setSortBy,
    toggleGroupByPackage,
  } = useFilterStore()
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(new Set())

  const { user } = useStytchUser()

  const { data: watchlist, isLoading: watchlistLoading } = useWatchlist()

  // Single combined feed call replaces usePackages + useReleases + buildVersionGroups
  const feedOptions = useMemo(() => {
    return showPrerelease ? undefined : { excludePrerelease: true }
  }, [showPrerelease])

  const { data: feedData, isLoading: feedLoading } = useFeed(feedOptions)

  const isLoading = feedLoading || (user ? watchlistLoading : false)

  // Transform feed groups into display-ready VersionGroups
  const versionGroups = useMemo(
    () => buildDisplayGroups(feedData?.groups ?? []),
    [feedData]
  )

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

  // Sort groups
  const sortedGroups = [...versionGroups].sort((a, b) => {
    if (sortBy === 'name') {
      return a.displayName.localeCompare(b.displayName)
    }
    // Sort by date (most recent first)
    return (
      new Date(b.lastUpdated ?? 0).getTime() -
      new Date(a.lastUpdated ?? 0).getTime()
    )
  })

  // Group by package for display
  const groupedByPackageMap = sortedGroups.reduce(
    (acc, group) => {
      if (!acc[group.displayName]) {
        acc[group.displayName] = []
      }
      acc[group.displayName].push(group)
      return acc
    },
    {} as Record<string, VersionGroup[]>
  )

  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader />

      <main className="py-8">
        <Container>
          {/* Hero Card for logged-out users */}
          <HeroCard />

          {/* Heading + Filters */}
          <div className="flex items-center justify-between gap-4 mb-6">
            {feedData?.isDefaultFeed ? (
              <h2 className="text-lg font-semibold text-text-primary">
                Recently Updated Packages
              </h2>
            ) : (
              <div />
            )}
            <div className="flex items-center gap-2">
              <FilterButton
                active={showPrerelease}
                onClick={togglePrerelease}
                tooltip={
                  showPrerelease ? 'Hide pre-releases' : 'Show pre-releases'
                }
                className="rounded-lg"
              >
                {showPrerelease ? (
                  <FlaskConical className="w-4 h-4" />
                ) : (
                  <FlaskConicalOff className="w-4 h-4" />
                )}
              </FilterButton>
              <FilterButton
                active={groupByPackage}
                onClick={toggleGroupByPackage}
                tooltip={
                  groupByPackage ? 'Disable grouping' : 'Group by package'
                }
                className="rounded-lg"
              >
                <Group className="w-4 h-4" />
              </FilterButton>
              <div className="flex items-center rounded-lg border border-border-default">
                <FilterButton
                  active={sortBy === 'name'}
                  onClick={() => setSortBy('name')}
                  tooltip="Sort by name"
                >
                  <ArrowDownAZ className="w-4 h-4" />
                </FilterButton>
                <div className="w-px h-5 bg-border-default" />
                <FilterButton
                  active={sortBy === 'date'}
                  onClick={() => setSortBy('date')}
                  tooltip="Sort by date"
                >
                  <CalendarArrowDown className="w-4 h-4" />
                </FilterButton>
              </div>
            </div>
          </div>
          {user && watchlist && watchlist.length === 0 && !watchlistLoading && (
            <div className="mb-6 rounded-lg border border-border-default bg-surface-primary p-4 text-center">
              <p className="text-sm text-text-secondary">
                Add packages to your watchlist to see relevant releases here.{' '}
                <Link
                  to="/watchlist"
                  className="font-medium text-brand-600 hover:text-brand-700 transition-colors"
                >
                  Go to Watchlist
                </Link>
              </p>
            </div>
          )}

          {/* Feed */}
          {isLoading ? (
            <div className="space-y-4">
              <SkeletonCard />
              <SkeletonCard />
              <SkeletonCard />
            </div>
          ) : groupByPackage ? (
            <div className="space-y-8">
              {Object.entries(groupedByPackageMap).map(
                ([packageName, groups]) => (
                  <section key={packageName}>
                    <h2 className="text-lg font-semibold text-text-primary mb-4 flex items-center gap-2">
                      <PackageIcon name={packageName} />
                      {packageName}
                      <span className="text-sm font-normal text-text-tertiary">
                        ({groups.length} version
                        {groups.length !== 1 && 's'})
                      </span>
                    </h2>
                    <div className="space-y-4">
                      {groups.map((group) => (
                        <SummaryCard
                          key={group.id}
                          group={group}
                          isExpanded={expandedGroups.has(group.id)}
                          onToggle={toggleExpanded}
                          showViewAllLink
                        />
                      ))}
                    </div>
                  </section>
                )
              )}
            </div>
          ) : (
            <div className="space-y-4">
              {sortedGroups.map((group) => (
                <SummaryCard
                  key={group.id}
                  group={group}
                  isExpanded={expandedGroups.has(group.id)}
                  onToggle={toggleExpanded}
                  showViewAllLink
                />
              ))}
            </div>
          )}

          {/* Empty State */}
          {!isLoading && versionGroups.length === 0 && (
            <div className="text-center py-16">
              <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-surface-tertiary flex items-center justify-center">
                <FlaskConicalOff className="w-8 h-8 text-text-tertiary" />
              </div>
              <h3 className="text-lg font-semibold text-text-primary mb-2">
                No releases found
              </h3>
              <p className="text-text-secondary">
                Try adjusting your filters to see more releases.
              </p>
            </div>
          )}
        </Container>
      </main>
    </div>
  )
}
