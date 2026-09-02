export type PrereleaseType =
  'canary' | 'beta' | 'alpha' | 'rc' | 'next' | 'preview'

export function formatDate(dateString: string | undefined | null): string {
  if (!dateString) return ''
  return new Date(dateString).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
}

export function formatDateLong(dateString: string | undefined): string {
  if (!dateString) return ''
  return new Date(dateString).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  })
}

export function formatDateTime(dateString: string | undefined): string {
  if (!dateString) return ''
  return new Date(dateString).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function formatRelativeTime(dateString: string | undefined): string {
  if (!dateString) return ''
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24))
  const diffHours = Math.floor(diffMs / (1000 * 60 * 60))

  if (diffHours < 1) return 'Just now'
  if (diffHours < 24) return `${diffHours}h ago`
  if (diffDays === 1) return 'Yesterday'
  if (diffDays < 7) return `${diffDays}d ago`
  if (diffDays < 30) return `${Math.floor(diffDays / 7)}w ago`
  return formatDate(dateString)
}

export function detectPrereleaseType(
  releases: { tag: string }[]
): PrereleaseType | undefined {
  for (const r of releases) {
    const lower = r.tag.toLowerCase()
    if (lower.includes('canary')) return 'canary'
    if (lower.includes('preview')) return 'preview'
    if (lower.includes('alpha')) return 'alpha'
    if (lower.includes('beta')) return 'beta'
    if (lower.includes('next')) return 'next'
    if (lower.includes('rc')) return 'rc'
  }
  return undefined
}
