import { memo } from 'react'
import { Link } from '@tanstack/react-router'
import { Share } from 'lucide-react'
import { LazyMarkdown as Markdown } from '../LazyMarkdown'
import { Badge, Card } from '../ui'
import { useToast } from '../Toast'
import { formatDate, formatRelativeTime } from '../../utils/dateFormat'

// ============================================================================
// Types
// ============================================================================

export interface SummaryGroup {
  id: string
  packageId: string
  displayName: string
  githubOwner: string
  githubRepo: string
  versionRange: string
  isPrerelease?: boolean
  prereleaseType?: string
  displaySummary: string
  hasSummary: boolean
  releaseCount: number
  lastUpdated?: string
  releases: Array<{
    id: string
    tag: string
    title?: string | null
    publishedAt?: string
  }>
}

// ============================================================================
// Sub-components
// ============================================================================

export function PackageIcon({ name }: { name: string }) {
  const icons: Record<string, { bg: string; text: string }> = {
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

export function PrereleaseTag({ type }: { type?: string }) {
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

// ============================================================================
// SummaryCard
// ============================================================================

export const SummaryCard = memo(function SummaryCard({
  group,
  isExpanded,
  onToggle,
  showViewAllLink = false,
}: {
  group: SummaryGroup
  isExpanded: boolean
  onToggle: (id: string) => void
  showViewAllLink?: boolean
}) {
  const { showToast } = useToast()

  const handleCopyLink = () => {
    const url = `${window.location.origin}/s/${group.packageId}`
    navigator.clipboard.writeText(url).then(() => {
      showToast('Link copied!', 'success')
    })
  }

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
            <button
              onClick={handleCopyLink}
              className="flex items-center gap-1.5 hover:text-text-secondary transition-colors"
              title="Copy share link"
            >
              <Share className="w-3.5 h-3.5" />
              Share
            </button>
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
          {showViewAllLink && (
            <div className="px-5 py-3 bg-surface-tertiary/30">
              <Link
                to="/packages/$owner/$repo"
                params={{
                  owner: group.githubOwner,
                  repo: group.githubRepo,
                }}
                className="text-sm text-brand-600 hover:text-brand-700 font-medium"
              >
                View all {group.releaseCount} releases →
              </Link>
            </div>
          )}
        </div>
      )}
    </Card>
  )
})
