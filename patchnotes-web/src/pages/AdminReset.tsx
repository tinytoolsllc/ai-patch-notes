import { useState, useEffect } from 'react'
import { useNavigate } from '@tanstack/react-router'
import {
  AppHeader,
  Breadcrumb,
  BreadcrumbSeparator,
  Container,
  Button,
  Card,
  Modal,
} from '../components/ui'
import {
  useDeletePackage,
  useResetSummaries,
  useResetReleases,
  useGetPackages,
} from '../api/hooks'
import type { PackageDto } from '../api/generated/model'
import { useIsAdmin } from '../utils/auth'

const PAGE_SIZE = 20

type ResetAction = 'summaries' | 'releases' | 'delete'

const ACTION_CONFIG: Record<
  ResetAction,
  {
    title: string
    description: string
    confirmLabel: string
    pendingLabel: string
  }
> = {
  summaries: {
    title: 'Reset Summaries',
    description:
      'This will mark all releases as needing new summaries and delete existing summaries. New summaries will be generated on the next sync.',
    confirmLabel: 'Reset Summaries',
    pendingLabel: 'Resetting...',
  },
  releases: {
    title: 'Reset Releases',
    description:
      'This will delete all releases and summaries for this package and re-fetch everything from GitHub on the next sync. This is equivalent to removing and re-adding the package.',
    confirmLabel: 'Reset Releases',
    pendingLabel: 'Resetting...',
  },
  delete: {
    title: 'Delete Package',
    description:
      'This will permanently delete this package and all its releases, summaries, and watchlist entries. This action cannot be undone.',
    confirmLabel: 'Delete',
    pendingLabel: 'Deleting...',
  },
}

// ── Confirm Modal ────────────────────────────────────────────

interface ConfirmModalProps {
  open: boolean
  onClose: () => void
  onConfirm: () => void
  pkg: PackageDto | null
  action: ResetAction
  isPending: boolean
}

function ConfirmModal({
  open,
  onClose,
  onConfirm,
  pkg,
  action,
  isPending,
}: ConfirmModalProps) {
  if (!open || !pkg) return null

  const config = ACTION_CONFIG[action]
  const isDestructive = action === 'delete'

  return (
    <Modal open={open} onClose={onClose} title={config.title}>
      <div className="space-y-4">
        <p className="text-text-secondary">
          <span className="font-medium text-text-primary">{pkg.name}</span>
          {' \u2014 '}
          {config.description}
        </p>
        <div className="flex justify-end gap-2 pt-2">
          <Button variant="secondary" onClick={onClose} disabled={isPending}>
            Cancel
          </Button>
          <Button
            onClick={onConfirm}
            disabled={isPending}
            className={
              isDestructive ? 'bg-major hover:bg-major/90 text-white' : ''
            }
          >
            {isPending ? config.pendingLabel : config.confirmLabel}
          </Button>
        </div>
      </div>
    </Modal>
  )
}

// ── Package Row ──────────────────────────────────────────────

interface PackageRowProps {
  pkg: PackageDto
  onAction: (pkg: PackageDto, action: ResetAction) => void
  pendingAction: { id: string; action: ResetAction } | null
}

function PackageRow({ pkg, onAction, pendingAction }: PackageRowProps) {
  const isPending = pendingAction?.id === pkg.id
  const githubUrl = `https://github.com/${pkg.githubOwner}/${pkg.githubRepo}`

  return (
    <tr className="border-b border-border-default last:border-0">
      <td className="py-3 px-4">
        <span className="font-medium text-text-primary">{pkg.name}</span>
      </td>
      <td className="py-3 px-4">
        <a
          href={githubUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="text-brand-600 hover:text-brand-700 hover:underline"
        >
          {pkg.githubOwner}/{pkg.githubRepo}
        </a>
      </td>
      <td className="py-3 px-4">
        <div className="flex gap-2 justify-end">
          <Button
            variant="secondary"
            size="sm"
            onClick={() => onAction(pkg, 'summaries')}
            disabled={isPending}
          >
            {isPending && pendingAction?.action === 'summaries'
              ? 'Resetting...'
              : 'Reset Summaries'}
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => onAction(pkg, 'releases')}
            disabled={isPending}
          >
            {isPending && pendingAction?.action === 'releases'
              ? 'Resetting...'
              : 'Reset Releases'}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onAction(pkg, 'delete')}
            disabled={isPending}
            className="text-major hover:text-major"
          >
            {isPending && pendingAction?.action === 'delete'
              ? 'Deleting...'
              : 'Delete'}
          </Button>
        </div>
      </td>
    </tr>
  )
}

// ── Pagination ───────────────────────────────────────────────

interface PaginationProps {
  offset: number
  total: number
  pageSize: number
  onPageChange: (newOffset: number) => void
}

function Pagination({
  offset,
  total,
  pageSize,
  onPageChange,
}: PaginationProps) {
  if (total <= pageSize) return null

  const currentPage = Math.floor(offset / pageSize) + 1
  const totalPages = Math.ceil(total / pageSize)

  return (
    <div className="flex items-center justify-between px-6 py-3 border-t border-border-default">
      <span className="text-sm text-text-secondary">
        Showing {offset + 1}\u2013{Math.min(offset + pageSize, total)} of{' '}
        {total}
      </span>
      <div className="flex gap-2">
        <Button
          variant="secondary"
          size="sm"
          disabled={offset === 0}
          onClick={() => onPageChange(Math.max(0, offset - pageSize))}
        >
          Previous
        </Button>
        <span className="flex items-center text-sm text-text-secondary px-2">
          {currentPage} / {totalPages}
        </span>
        <Button
          variant="secondary"
          size="sm"
          disabled={offset + pageSize >= total}
          onClick={() => onPageChange(offset + pageSize)}
        >
          Next
        </Button>
      </div>
    </div>
  )
}

// ── Main Page ────────────────────────────────────────────────

export function AdminReset() {
  const { isAdmin, isLoading: authLoading } = useIsAdmin()
  const navigate = useNavigate()
  const [offset, setOffset] = useState(0)

  const { data: response, isLoading } = useGetPackages(
    { limit: PAGE_SIZE, offset },
    {
      query: {
        select: (res) => res.data,
      },
    }
  )

  const resetSummaries = useResetSummaries()
  const resetReleases = useResetReleases()
  const deletePackage = useDeletePackage()

  const [confirmState, setConfirmState] = useState<{
    pkg: PackageDto
    action: ResetAction
  } | null>(null)
  const [pendingAction, setPendingAction] = useState<{
    id: string
    action: ResetAction
  } | null>(null)

  useEffect(() => {
    if (!authLoading && !isAdmin) {
      navigate({ to: '/' })
    }
  }, [authLoading, isAdmin, navigate])

  if (authLoading || !isAdmin) {
    return (
      <div className="min-h-screen bg-surface-secondary flex items-center justify-center">
        <p className="text-text-secondary">
          {authLoading ? 'Checking access...' : 'Redirecting...'}
        </p>
      </div>
    )
  }

  const handleAction = (pkg: PackageDto, action: ResetAction) => {
    setConfirmState({ pkg, action })
  }

  const handleConfirm = async () => {
    if (!confirmState) return
    const { pkg, action } = confirmState
    setPendingAction({ id: pkg.id, action })
    setConfirmState(null)

    try {
      if (action === 'summaries') {
        await resetSummaries.mutateAsync(pkg.id)
      } else if (action === 'releases') {
        await resetReleases.mutateAsync(pkg.id)
      } else {
        await deletePackage.mutateAsync(pkg.id)
      }
    } finally {
      setPendingAction(null)
    }
  }

  const packages = response?.items ?? []
  const total = response?.total ?? 0

  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader
        breadcrumbs={
          <>
            <Breadcrumb to="/admin">Admin</Breadcrumb>
            <BreadcrumbSeparator />
            <Breadcrumb>Reset Data</Breadcrumb>
          </>
        }
      />

      <main className="py-8">
        <Container>
          <div className="mb-6">
            <h1 className="text-xl font-semibold text-text-primary">
              Reset Package Data
            </h1>
            <p className="text-sm text-text-secondary mt-1">
              Reset summaries, releases, or delete packages. Changes take effect
              on the next sync run.
            </p>
          </div>

          <Card padding="none">
            <div className="px-6 py-4 border-b border-border-default">
              <h2 className="text-lg font-semibold text-text-primary">
                Packages
              </h2>
              <p className="text-sm text-text-secondary mt-1">
                {isLoading
                  ? 'Loading...'
                  : `${total} package${total !== 1 ? 's' : ''}`}
              </p>
            </div>

            {isLoading ? (
              <div className="p-6 text-text-secondary">Loading packages...</div>
            ) : packages.length === 0 ? (
              <div className="p-6 text-text-secondary">No packages found.</div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead>
                    <tr className="border-b border-border-default bg-surface-secondary">
                      <th className="py-3 px-4 text-left text-sm font-medium text-text-secondary">
                        Name
                      </th>
                      <th className="py-3 px-4 text-left text-sm font-medium text-text-secondary">
                        GitHub Repo
                      </th>
                      <th className="py-3 px-4 text-right text-sm font-medium text-text-secondary">
                        Actions
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {packages.map((pkg) => (
                      <PackageRow
                        key={pkg.id}
                        pkg={pkg}
                        onAction={handleAction}
                        pendingAction={pendingAction}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <Pagination
              offset={offset}
              total={total}
              pageSize={PAGE_SIZE}
              onPageChange={setOffset}
            />
          </Card>
        </Container>
      </main>

      <ConfirmModal
        open={!!confirmState}
        onClose={() => setConfirmState(null)}
        onConfirm={handleConfirm}
        pkg={confirmState?.pkg ?? null}
        action={confirmState?.action ?? 'summaries'}
        isPending={!!pendingAction}
      />
    </div>
  )
}
