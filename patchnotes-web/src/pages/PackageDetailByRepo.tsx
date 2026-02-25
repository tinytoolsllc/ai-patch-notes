import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { LazyMarkdown as Markdown } from '../components/LazyMarkdown'
import { AppHeader, Container, Card, Badge } from '../components/ui'
import { usePackageByOwnerRepo } from '../api/hooks'
import type {
  PackageDetailGroupDto,
  PackageDetailReleaseDto,
} from '../api/generated/model'
import {
  formatDate,
  formatRelativeTime,
  detectPrereleaseType,
} from '../utils/dateFormat'

interface PackageDetailByRepoProps {
  owner: string
  repo: string
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

function ReleaseItem({ release }: { release: PackageDetailReleaseDto }) {
  const [expanded, setExpanded] = useState(false)

  return (
    <div className="px-5 py-3 hover:bg-surface-tertiary/50 transition-colors">
      <div
        className="flex items-center justify-between gap-4 cursor-pointer"
        onClick={() => release.body && setExpanded(!expanded)}
      >
        <div className="flex items-center gap-3">
          <code className="text-sm font-mono text-brand-600 bg-brand-50 dark:bg-brand-900/20 px-2 py-0.5 rounded">
            {release.tag}
          </code>
          <span className="text-sm text-text-primary">
            {release.title ?? release.tag}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <time className="text-xs text-text-tertiary whitespace-nowrap">
            {formatDate(release.publishedAt)}
          </time>
          {release.body && (
            <svg
              className={`w-4 h-4 text-text-tertiary transition-transform ${expanded ? 'rotate-180' : ''}`}
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
          )}
        </div>
      </div>
      {expanded && release.body && (
        <div className="mt-3 pl-2 text-sm text-text-secondary prose prose-sm dark:prose-invert max-w-none">
          <Markdown>{release.body}</Markdown>
        </div>
      )}
    </div>
  )
}

function VersionGroupCard({ group }: { group: PackageDetailGroupDto }) {
  const [isExpanded, setIsExpanded] = useState(false)
  const prereleaseType = group.isPrerelease
    ? detectPrereleaseType(group.releases)
    : undefined

  const displaySummary =
    group.summary ??
    (() => {
      const titles = group.releases
        .slice(0, 3)
        .map((r) => r.title || r.tag)
        .join(', ')
      const extra =
        (group.releaseCount ?? 0) > 3
          ? ` and ${(group.releaseCount ?? 0) - 3} more`
          : ''
      return `${group.releaseCount ?? 0} release${(group.releaseCount ?? 0) !== 1 ? 's' : ''}: ${titles}${extra}.`
    })()

  return (
    <Card
      padding="none"
      className="overflow-hidden hover:shadow-md transition-shadow"
    >
      <div className="p-5">
        <div className="flex items-start justify-between gap-4 mb-3">
          <div className="flex items-center gap-2">
            <span className="text-sm font-mono text-text-secondary">
              {group.versionRange}
            </span>
            {group.isPrerelease ? (
              <PrereleaseTag type={prereleaseType} />
            ) : (
              <Badge variant="minor">stable</Badge>
            )}
          </div>
          <time
            dateTime={group.lastUpdated}
            title={formatDate(group.lastUpdated)}
            className="text-sm text-text-tertiary whitespace-nowrap"
          >
            {formatRelativeTime(group.lastUpdated)}
          </time>
        </div>

        {group.summary ? (
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
              {displaySummary}
            </Markdown>
          </div>
        ) : (
          <p className="text-sm text-text-secondary leading-relaxed">
            {displaySummary}
          </p>
        )}

        <div className="flex items-center justify-between mt-4 pt-4 border-t border-border-muted">
          <span className="text-sm text-text-tertiary">
            {group.releaseCount ?? 0} release
            {(group.releaseCount ?? 0) !== 1 && 's'}
          </span>
          <button
            onClick={() => setIsExpanded(!isExpanded)}
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

      {isExpanded && (
        <div className="border-t border-border-default bg-surface-secondary/50">
          <div className="divide-y divide-border-muted">
            {group.releases.map((release) => (
              <ReleaseItem key={release.id} release={release} />
            ))}
          </div>
        </div>
      )}
    </Card>
  )
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
            {data.groups.map((group) => (
              <VersionGroupCard
                key={`${group.majorVersion}-${group.isPrerelease}`}
                group={group}
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
