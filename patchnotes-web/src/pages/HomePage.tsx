import { useState, useMemo, useCallback, memo } from 'react'
import { Link } from '@tanstack/react-router'
import { useStytchUser } from '@stytch/react'
import { LazyMarkdown as Markdown } from '../components/LazyMarkdown'
import {
  FlaskConical,
  FlaskConicalOff,
  ArrowDownAZ,
  CalendarArrowDown,
  Group,
} from 'lucide-react'
import {
  AppHeader,
  Container,
  Badge,
  Card,
  Tooltip,
} from '../components/ui'
import {
  formatDate,
  formatRelativeTime,
  detectPrereleaseType,
  type PrereleaseType,
} from '../utils/dateFormat'
import { HeroCard } from '../components/landing/HeroCard'
import { useFilterStore } from '../stores/filterStore'
import { useWatchlist, useFeed } from '../api/hooks'
import type { FeedGroupDto } from '../api/hooks'

// ============================================================================
// Types
// ============================================================================

interface VersionGroup extends FeedGroupDto {
  id: string
  displayName: string
  prereleaseType?: PrereleaseType
  displaySummary: string
  hasSummary: boolean
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
    if (!displaySummary) {
      const titles = g.releases
        .slice(0, 3)
        .map((r) => r.title || r.tag)
        .join(', ')
      const extra = g.releaseCount > 3 ? ` and ${g.releaseCount - 3} more` : ''
      displaySummary = `${g.releaseCount} release${g.releaseCount !== 1 ? 's' : ''} in this version: ${titles}${extra}.`
    }

    return {
      ...g,
      id: `${g.packageId}-${g.majorVersion}-${g.isPrerelease}`,
      displayName,
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

function PackageIcon({ name }: { name: string }) {
  // Brand colors for well-known packages
  const icons: Record<string, { bg: string; text: string; icon?: string }> = {
    'Next.js': {
      bg: 'bg-black',
      text: 'text-white',
    },
    React: {
      bg: 'bg-sky-500/15',
      text: 'text-sky-600 dark:text-sky-400',
    },
    TypeScript: {
      bg: 'bg-blue-500/15',
      text: 'text-blue-600 dark:text-blue-400',
    },
    Vite: {
      bg: 'bg-violet-500/15',
      text: 'text-violet-600 dark:text-violet-400',
    },
  }
  const { bg, text } = icons[name] || {
    bg: 'bg-brand-100 dark:bg-brand-900/30',
    text: 'text-brand-600 dark:text-brand-400',
  }

  const initial = name.charAt(0).toUpperCase()

  return (
    <div
      className={`w-10 h-10 rounded-lg flex items-center justify-center font-semibold text-lg ${bg} ${text}`}
    >
      {initial}
    </div>
  )
}

function PrereleaseTag({ type }: { type?: string }) {
  if (!type) return null

  const colors: Record<string, string> = {
    canary:
      'bg-orange-100 text-orange-900 dark:bg-orange-900/40 dark:text-orange-200',
    beta: 'bg-blue-50 text-blue-800 ring-1 ring-inset ring-blue-600/20 dark:bg-blue-900/30 dark:text-blue-300 dark:ring-blue-500/30',
    alpha:
      'bg-purple-50 text-purple-800 ring-1 ring-inset ring-purple-600/20 dark:bg-purple-900/30 dark:text-purple-300 dark:ring-purple-500/30',
    rc: 'bg-emerald-50 text-emerald-800 ring-1 ring-inset ring-emerald-600/20 dark:bg-emerald-900/30 dark:text-emerald-300 dark:ring-emerald-500/30',
    next: 'bg-pink-50 text-pink-800 ring-1 ring-inset ring-pink-600/20 dark:bg-pink-900/30 dark:text-pink-300 dark:ring-pink-500/30',
    preview:
      'bg-amber-50 text-amber-800 ring-1 ring-inset ring-amber-600/20 dark:bg-amber-900/30 dark:text-amber-300 dark:ring-amber-500/30',
  }

  return (
    <span
      className={`inline-flex items-center px-2 py-0.5 text-xs font-medium rounded-full ${colors[type] || colors.beta}`}
    >
      {type}
    </span>
  )
}

const SummaryCard = memo(function SummaryCard({
  group,
  isExpanded,
  onToggle,
}: {
  group: VersionGroup
  isExpanded: boolean
  onToggle: (id: string) => void
}) {
  return (
    <Card
      padding="none"
      className="overflow-hidden hover:shadow-md transition-shadow"
    >
      {/* Main Summary Section */}
      <div className="p-5">
        {/* Header */}
        <div className="flex items-start justify-between gap-4 mb-3">
          <div className="flex items-center gap-3">
            <PackageIcon name={group.displayName} />
            <div>
              <div className="flex items-center gap-2">
                <Link
                  to="/packages/$owner"
                  params={{ owner: group.githubOwner }}
                  className="font-semibold text-text-primary hover:text-brand-600 transition-colors"
                >
                  {group.displayName}
                </Link>
                <span className="text-sm font-mono text-text-secondary">
                  {group.versionRange}
                </span>
              </div>
              <div className="flex items-center gap-2 mt-0.5">
                {group.isPrerelease ? (
                  <PrereleaseTag type={group.prereleaseType} />
                ) : (
                  <Badge variant="minor">stable</Badge>
                )}
              </div>
            </div>
          </div>
          <time
            dateTime={group.lastUpdated}
            title={formatDate(group.lastUpdated)}
            className="text-sm text-text-tertiary whitespace-nowrap"
          >
            {formatRelativeTime(group.lastUpdated)}
          </time>
        </div>

        {/* Summary */}
        {group.hasSummary ? (
          <div className="text-sm text-text-secondary leading-relaxed">
            <Markdown
              components={{
                h2: ({ children }) => (
                  <h4 className="text-xs font-semibold uppercase tracking-wide text-text-tertiary mt-3 first:mt-0 mb-1">
                    {children}
                  </h4>
                ),
                p: ({ children }) => (
                  <p className="mb-2 last:mb-0">{children}</p>
                ),
                ul: ({ children }) => (
                  <ul className="list-disc list-inside mb-2 last:mb-0 space-y-0.5">
                    {children}
                  </ul>
                ),
                li: ({ children }) => <li>{children}</li>,
              }}
            >
              {group.displaySummary}
            </Markdown>
          </div>
        ) : (
          <p className="text-sm text-text-secondary leading-relaxed">
            {group.displaySummary}
          </p>
        )}

        {/* Footer */}
        <div className="flex items-center justify-between mt-4 pt-4 border-t border-border-muted">
          <div className="flex items-center gap-4 text-sm text-text-tertiary">
            <span className="flex items-center gap-1.5">
              <svg
                className="w-4 h-4"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={1.5}
                  d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A2 2 0 013 12V7a4 4 0 014-4z"
                />
              </svg>
              {group.releaseCount} release{group.releaseCount !== 1 && 's'}
            </span>
          </div>
          <button
            onClick={() => onToggle(group.id)}
            className="flex items-center gap-1.5 text-sm font-medium text-brand-600 hover:text-brand-700 transition-colors"
          >
            {isExpanded ? 'Hide releases' : 'Show releases'}
            <svg
              className={`w-4 h-4 transition-transform ${isExpanded ? 'rotate-180' : ''}`}
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M19 9l-7 7-7-7"
              />
            </svg>
          </button>
        </div>
      </div>

      {/* Expanded Releases */}
      {isExpanded && (
        <div className="border-t border-border-default bg-surface-secondary/50">
          <div className="divide-y divide-border-muted">
            {group.releases.map((release) => (
              <div
                key={release.id}
                className="px-5 py-3 hover:bg-surface-tertiary/50 transition-colors"
              >
                <div className="flex items-center justify-between gap-4">
                  <div className="flex items-center gap-3">
                    <code className="text-sm font-mono text-brand-600 bg-brand-50 dark:bg-brand-900/20 px-2 py-0.5 rounded">
                      {release.tag}
                    </code>
                    <span className="text-sm text-text-primary">
                      {release.title ?? release.tag}
                    </span>
                  </div>
                  <time className="text-xs text-text-tertiary whitespace-nowrap">
                    {formatDate(release.publishedAt)}
                  </time>
                </div>
              </div>
            ))}
          </div>
          <div className="px-5 py-3 bg-surface-tertiary/30">
            <Link
              to="/packages/$owner/$repo"
              params={{ owner: group.githubOwner, repo: group.githubRepo }}
              className="text-sm text-brand-600 hover:text-brand-700 font-medium"
            >
              View all {group.releaseCount} releases →
            </Link>
          </div>
        </div>
      )}
    </Card>
  )
})

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
    heroDismissed,
    togglePrerelease,
    setSortBy,
    toggleGroupByPackage,
    dismissHero,
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
    return new Date(b.lastUpdated).getTime() - new Date(a.lastUpdated).getTime()
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
          {!user && !heroDismissed && <HeroCard onDismiss={dismissHero} />}

          {/* Filters */}
          <div className="flex items-center justify-end gap-2 mb-6">
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
              tooltip={groupByPackage ? 'Disable grouping' : 'Group by package'}
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

          {feedData?.isDefaultFeed && (
            <h2 className="text-lg font-semibold text-text-primary mb-4">
              Recently Updated Packages
            </h2>
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
