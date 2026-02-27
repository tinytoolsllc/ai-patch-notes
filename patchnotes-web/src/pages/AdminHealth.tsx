import { useState, useEffect, useCallback } from 'react'
import { Link, useNavigate } from '@tanstack/react-router'
import {
  AppHeader,
  Breadcrumb,
  Container,
  Button,
  Card,
} from '../components/ui'
import { useIsAdmin } from '../utils/auth'
import {
  usePackageHealth,
  useResetPackageSync,
  useDisablePackageSync,
  getGetPackagesHealthQueryKey,
} from '../api/hooks'
import { useQueryClient } from '@tanstack/react-query'
import type { PackageHealthDto } from '../api/generated/model'

// ── Helpers ───────────────────────────────────────────────────

function formatRelativeTime(dateString: string | null | undefined): string {
  if (!dateString) return '—'
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24))
  const diffHours = Math.floor(diffMs / (1000 * 60 * 60))
  const diffMinutes = Math.floor(diffMs / (1000 * 60))

  if (diffDays > 30) return date.toLocaleDateString()
  if (diffDays > 0) return `${diffDays}d ago`
  if (diffHours > 0) return `${diffHours}h ago`
  if (diffMinutes > 0) return `${diffMinutes}m ago`
  return 'just now'
}

type HealthStatus = 'healthy' | 'failing' | 'disabled'

function getStatus(pkg: PackageHealthDto): HealthStatus {
  if (pkg.isSyncDisabled) return 'disabled'
  if ((pkg.consecutiveFailures ?? 0) > 0) return 'failing'
  return 'healthy'
}

// ── Status Badge ─────────────────────────────────────────────

function StatusBadge({ status }: { status: HealthStatus }) {
  const styles: Record<HealthStatus, string> = {
    healthy: 'bg-minor/15 text-minor border border-minor/30',
    failing: 'bg-[#b45309]/15 text-[#b45309] border border-[#b45309]/30',
    disabled: 'bg-major/15 text-major border border-major/30',
  }
  const labels: Record<HealthStatus, string> = {
    healthy: 'Healthy',
    failing: 'Failing',
    disabled: 'Disabled',
  }

  return (
    <span
      className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${styles[status]}`}
    >
      {labels[status]}
    </span>
  )
}

// ── Failure Message Cell ──────────────────────────────────────

function FailureMessageCell({
  message,
}: {
  message: string | null | undefined
}) {
  const [expanded, setExpanded] = useState(false)

  if (!message) return <span className="text-text-tertiary">—</span>

  const truncated = message.length > 80

  return (
    <div className="max-w-xs">
      <p
        className={`text-xs text-text-secondary font-mono ${!expanded && truncated ? 'line-clamp-2' : ''}`}
      >
        {message}
      </p>
      {truncated && (
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="text-xs text-brand-600 hover:text-brand-700 mt-0.5"
        >
          {expanded ? 'Show less' : 'Show more'}
        </button>
      )}
    </div>
  )
}

// ── Package Health Row ────────────────────────────────────────

interface HealthRowProps {
  pkg: PackageHealthDto
  onReset: (id: string) => void
  onDisable: (id: string) => void
  isResetting: boolean
  isDisabling: boolean
}

function HealthRow({
  pkg,
  onReset,
  onDisable,
  isResetting,
  isDisabling,
}: HealthRowProps) {
  const status = getStatus(pkg)
  const githubUrl = `https://github.com/${pkg.githubOwner}/${pkg.githubRepo}`

  return (
    <tr className="border-b border-border-default last:border-0">
      <td className="py-3 px-4">
        <div>
          <a
            href={githubUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-brand-600 hover:text-brand-700 hover:underline text-sm"
          >
            {pkg.name || `${pkg.githubOwner}/${pkg.githubRepo}`}
          </a>
          <div className="text-xs text-text-tertiary mt-0.5">
            {pkg.githubOwner}/{pkg.githubRepo}
          </div>
        </div>
      </td>
      <td className="py-3 px-4">
        <StatusBadge status={status} />
      </td>
      <td className="py-3 px-4 text-sm text-text-secondary text-right">
        {(pkg.consecutiveFailures ?? 0) > 0 ? (
          <span className="font-medium text-[#b45309]">
            {pkg.consecutiveFailures}
          </span>
        ) : (
          <span className="text-text-tertiary">0</span>
        )}
      </td>
      <td className="py-3 px-4 text-sm text-text-secondary whitespace-nowrap">
        {formatRelativeTime(pkg.lastFailureAt)}
      </td>
      <td className="py-3 px-4">
        <FailureMessageCell message={pkg.lastFailureMessage} />
      </td>
      <td className="py-3 px-4 text-sm text-text-secondary whitespace-nowrap">
        {formatRelativeTime(pkg.lastFetchedAt)}
      </td>
      <td className="py-3 px-4">
        <div className="flex gap-2 justify-end">
          <Button
            variant="secondary"
            size="sm"
            onClick={() => onReset(pkg.id)}
            disabled={isResetting || isDisabling}
          >
            {isResetting ? 'Resetting...' : 'Reset'}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onDisable(pkg.id)}
            disabled={isResetting || isDisabling || pkg.isSyncDisabled}
            className="text-major hover:text-major"
          >
            {isDisabling ? 'Disabling...' : 'Disable'}
          </Button>
        </div>
      </td>
    </tr>
  )
}

// ── Filter Controls ───────────────────────────────────────────

type FilterMode = 'all' | 'failing' | 'disabled'

function FilterBar({
  filter,
  onChange,
  counts,
}: {
  filter: FilterMode
  onChange: (f: FilterMode) => void
  counts: { all: number; failing: number; disabled: number }
}) {
  const options: { value: FilterMode; label: string; count: number }[] = [
    { value: 'all', label: 'All', count: counts.all },
    { value: 'failing', label: 'Failing', count: counts.failing },
    { value: 'disabled', label: 'Disabled', count: counts.disabled },
  ]

  return (
    <div className="flex gap-1">
      {options.map((opt) => (
        <button
          key={opt.value}
          type="button"
          onClick={() => onChange(opt.value)}
          className={`px-3 py-1.5 text-sm rounded-md font-medium transition-colors ${
            filter === opt.value
              ? 'bg-brand-500 text-white'
              : 'text-text-secondary hover:bg-surface-tertiary'
          }`}
        >
          {opt.label}
          <span
            className={`ml-1.5 text-xs ${filter === opt.value ? 'opacity-80' : 'text-text-tertiary'}`}
          >
            {opt.count}
          </span>
        </button>
      ))}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────

export function AdminHealth() {
  const { isAdmin, isLoading: authLoading } = useIsAdmin()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<FilterMode>('all')
  const [resettingId, setResettingId] = useState<string | null>(null)
  const [disablingId, setDisablingId] = useState<string | null>(null)

  const { data: healthResponse, isLoading } = usePackageHealth()
  const resetSync = useResetPackageSync()
  const disableSync = useDisablePackageSync()

  const packages = healthResponse?.status === 200 ? healthResponse.data : []

  // Auto-refresh every 30 seconds
  useEffect(() => {
    const timer = setInterval(() => {
      queryClient.invalidateQueries({
        queryKey: getGetPackagesHealthQueryKey(),
      })
    }, 30_000)
    return () => clearInterval(timer)
  }, [queryClient])

  // Auth gate
  useEffect(() => {
    if (!authLoading && !isAdmin) {
      navigate({ to: '/' })
    }
  }, [authLoading, isAdmin, navigate])

  const handleRefresh = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: getGetPackagesHealthQueryKey() })
  }, [queryClient])

  const handleReset = useCallback(
    async (id: string) => {
      setResettingId(id)
      try {
        await resetSync.mutateAsync({ id })
        queryClient.invalidateQueries({
          queryKey: getGetPackagesHealthQueryKey(),
        })
      } finally {
        setResettingId(null)
      }
    },
    [resetSync, queryClient]
  )

  const handleDisable = useCallback(
    async (id: string) => {
      setDisablingId(id)
      try {
        await disableSync.mutateAsync({ id })
        queryClient.invalidateQueries({
          queryKey: getGetPackagesHealthQueryKey(),
        })
      } finally {
        setDisablingId(null)
      }
    },
    [disableSync, queryClient]
  )

  if (authLoading || !isAdmin) {
    return (
      <div className="min-h-screen bg-surface-secondary flex items-center justify-center">
        <p className="text-text-secondary">
          {authLoading ? 'Checking access...' : 'Redirecting...'}
        </p>
      </div>
    )
  }

  const counts = {
    all: packages.length,
    failing: packages.filter(
      (p) => !p.isSyncDisabled && (p.consecutiveFailures ?? 0) > 0
    ).length,
    disabled: packages.filter((p) => p.isSyncDisabled).length,
  }

  const filtered = packages.filter((p) => {
    if (filter === 'failing')
      return !p.isSyncDisabled && (p.consecutiveFailures ?? 0) > 0
    if (filter === 'disabled') return p.isSyncDisabled
    return true
  })

  // Sort: disabled first, then by failure count desc, then alphabetical
  const sorted = [...filtered].sort((a, b) => {
    const sa = getStatus(a)
    const sb = getStatus(b)
    if (sa === 'disabled' && sb !== 'disabled') return -1
    if (sb === 'disabled' && sa !== 'disabled') return 1
    const fa = a.consecutiveFailures ?? 0
    const fb = b.consecutiveFailures ?? 0
    if (fa !== fb) return fb - fa
    return (a.name || '').localeCompare(b.name || '')
  })

  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader
        breadcrumbs={
          <>
            <Link to="/admin">
              <Breadcrumb>Admin</Breadcrumb>
            </Link>
            <Breadcrumb>Package Health</Breadcrumb>
          </>
        }
      />

      <main className="py-8">
        <Container>
          <div className="flex items-center justify-between mb-6">
            <div>
              <h1 className="text-xl font-semibold text-text-primary">
                Package Health
              </h1>
              <p className="text-sm text-text-secondary mt-1">
                Sync status for all tracked packages
              </p>
            </div>
            <div className="flex items-center gap-3">
              <Link to="/admin/emails">
                <Button variant="secondary" size="sm">
                  Email Templates
                </Button>
              </Link>
              <Button variant="secondary" size="sm" onClick={handleRefresh}>
                Refresh
              </Button>
            </div>
          </div>

          <Card padding="none">
            <div className="px-6 py-4 border-b border-border-default flex items-center justify-between gap-4">
              <FilterBar filter={filter} onChange={setFilter} counts={counts} />
              <p className="text-sm text-text-secondary shrink-0">
                {isLoading
                  ? 'Loading...'
                  : `${sorted.length} package${sorted.length !== 1 ? 's' : ''}`}
              </p>
            </div>

            {isLoading ? (
              <div className="p-6 text-text-secondary">
                Loading health data...
              </div>
            ) : sorted.length === 0 ? (
              <div className="p-6 text-text-secondary">
                {filter === 'all'
                  ? 'No packages tracked.'
                  : `No ${filter} packages.`}
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead>
                    <tr className="border-b border-border-default bg-surface-secondary">
                      <th className="py-3 px-4 text-left text-sm font-medium text-text-secondary">
                        Package
                      </th>
                      <th className="py-3 px-4 text-left text-sm font-medium text-text-secondary">
                        Status
                      </th>
                      <th className="py-3 px-4 text-right text-sm font-medium text-text-secondary">
                        Failures
                      </th>
                      <th className="py-3 px-4 text-left text-sm font-medium text-text-secondary">
                        Last Failure
                      </th>
                      <th className="py-3 px-4 text-left text-sm font-medium text-text-secondary">
                        Last Failure Message
                      </th>
                      <th className="py-3 px-4 text-left text-sm font-medium text-text-secondary">
                        Last Synced
                      </th>
                      <th className="py-3 px-4 text-right text-sm font-medium text-text-secondary">
                        Actions
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {sorted.map((pkg) => (
                      <HealthRow
                        key={pkg.id}
                        pkg={pkg}
                        onReset={handleReset}
                        onDisable={handleDisable}
                        isResetting={resettingId === pkg.id}
                        isDisabling={disablingId === pkg.id}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </Container>
      </main>
    </div>
  )
}
