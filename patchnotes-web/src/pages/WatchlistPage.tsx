import { useState, useEffect, useRef } from 'react'
import { Link, useNavigate } from '@tanstack/react-router'
import { useStytchUser } from '@stytch/react'
import { useDebouncedValue } from '@tanstack/react-pacer'
import { Search, X, Trash2, Plus, Star, Loader2 } from 'lucide-react'
import { Header, HeaderTitle, Container, Button, Card } from '../components/ui'
import { ThemeToggle } from '../components/theme'
import { UserMenu } from '../components/auth'
import { Logo } from '../components/landing/Logo'
import {
  useWatchlist,
  useRemoveFromWatchlist,
  useGithubSearch,
  useAddFromGithub,
} from '../api/hooks'
import type { WatchlistPackageDto } from '../api/generated/model'
import type { GitHubRepoSearchResultDto } from '../api/generated/model'

// ── Helpers ──────────────────────────────────────────────────

function formatStars(count: number | undefined): string {
  if (count == null) return ''
  if (count >= 1000) return `${(count / 1000).toFixed(1)}k`
  return String(count)
}

// ── Search Result Item ───────────────────────────────────────

function SearchResultItem({
  result,
  isWatched,
  isAdding,
  onAdd,
}: {
  result: GitHubRepoSearchResultDto
  isWatched: boolean
  isAdding: boolean
  onAdd: () => void
}) {
  return (
    <div className="flex items-center justify-between gap-4 px-4 py-3 border-b border-border-muted last:border-0">
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="font-medium text-text-primary text-sm truncate">
            {result.owner}/{result.repo}
          </span>
          {result.starCount != null && result.starCount > 0 && (
            <span className="flex items-center gap-0.5 text-xs text-text-tertiary flex-shrink-0">
              <Star className="w-3 h-3" />
              {formatStars(result.starCount)}
            </span>
          )}
        </div>
        {result.description && (
          <p className="text-xs text-text-secondary mt-0.5 line-clamp-1">
            {result.description}
          </p>
        )}
      </div>
      {isWatched ? (
        <span className="text-xs text-text-tertiary flex-shrink-0">
          Already added
        </span>
      ) : (
        <Button
          size="sm"
          variant="secondary"
          onClick={onAdd}
          disabled={isAdding}
          className="flex-shrink-0"
        >
          {isAdding ? (
            <Loader2 className="w-3.5 h-3.5 animate-spin" />
          ) : (
            <Plus className="w-3.5 h-3.5" />
          )}
          Add
        </Button>
      )}
    </div>
  )
}

// ── Watched Package Item ─────────────────────────────────────

function WatchedPackageItem({
  pkg,
  onRemove,
  isRemoving,
}: {
  pkg: WatchlistPackageDto
  onRemove: () => void
  isRemoving: boolean
}) {
  const displayName = pkg.npmName ?? `${pkg.githubOwner}/${pkg.githubRepo}`

  return (
    <div className="flex items-center justify-between gap-3 px-4 py-2.5 border-b border-border-muted last:border-0">
      <div className="min-w-0 flex-1">
        <Link
          to="/packages/$owner/$repo"
          params={{ owner: pkg.githubOwner, repo: pkg.githubRepo }}
          className="text-sm font-medium text-text-primary hover:text-brand-600 transition-colors truncate block"
        >
          {displayName}
        </Link>
        <span className="text-xs text-text-tertiary">
          {pkg.githubOwner}/{pkg.githubRepo}
        </span>
      </div>
      <button
        onClick={onRemove}
        disabled={isRemoving}
        className="flex-shrink-0 p-1 rounded text-text-tertiary hover:text-major hover:bg-surface-tertiary transition-colors disabled:opacity-50"
        title="Remove from watchlist"
      >
        <Trash2 className="w-4 h-4" />
      </button>
    </div>
  )
}

// ── Main Component ───────────────────────────────────────────

export function WatchlistPage() {
  const { user, isInitialized } = useStytchUser()
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [debouncedQuery] = useDebouncedValue(query.trim(), { wait: 300 })
  const inputRef = useRef<HTMLInputElement>(null)
  const [addingGithub, setAddingGithub] = useState<string | null>(null)

  const { data: watchlist, isLoading: watchlistLoading } = useWatchlist()
  const removeFromWatchlist = useRemoveFromWatchlist()
  const addFromGithub = useAddFromGithub()

  const { data: searchData, isFetching: isSearching } =
    useGithubSearch(debouncedQuery)

  // Redirect if not authenticated
  useEffect(() => {
    if (isInitialized && !user) {
      navigate({ to: '/login' })
    }
  }, [isInitialized, user, navigate])

  if (!isInitialized || !user) {
    return (
      <div className="min-h-screen bg-surface-secondary flex items-center justify-center">
        <p className="text-text-secondary">Loading...</p>
      </div>
    )
  }

  // Build a set of watched owner/repo pairs for matching search results
  const watchedRepos = new Set<string>()
  if (watchlist) {
    for (const pkg of watchlist) {
      watchedRepos.add(`${pkg.githubOwner}/${pkg.githubRepo}`.toLowerCase())
    }
  }

  // Extract search results from the response
  const searchResults: GitHubRepoSearchResultDto[] =
    searchData && 'data' in searchData
      ? ((searchData as { data: GitHubRepoSearchResultDto[] }).data ?? [])
      : []

  const showSearchResults = debouncedQuery.length >= 2

  const handleAddFromGithub = (result: GitHubRepoSearchResultDto) => {
    const key = `${result.owner}/${result.repo}`
    setAddingGithub(key)
    addFromGithub.mutate(
      { owner: result.owner, repo: result.repo },
      {
        onSettled: () => setAddingGithub(null),
      }
    )
  }

  const handleRemove = (packageId: string) => {
    removeFromWatchlist.mutate(packageId)
  }

  return (
    <div className="min-h-screen bg-surface-secondary">
      <Header
        breadcrumb={
          <>
            <Link to="/" className="hover:text-text-primary transition-colors">
              Home
            </Link>
            <span className="text-text-tertiary">/</span>
            <span className="text-text-primary">Watchlist</span>
          </>
        }
      >
        <Link
          to="/"
          className="flex items-center gap-2.5 hover:opacity-80 transition-opacity"
        >
          <Logo size={36} />
          <div>
            <HeaderTitle>My Release Notes</HeaderTitle>
            <p className="text-2xs text-text-tertiary leading-tight">
              by Tiny Tools
            </p>
          </div>
        </Link>
        <div className="flex items-center gap-2">
          <ThemeToggle />
          <UserMenu />
        </div>
      </Header>

      <main className="py-8">
        <Container>
          <div className="max-w-2xl mx-auto space-y-6">
            {/* Search Section */}
            <Card padding="none">
              <div className="p-4 border-b border-border-default">
                <h2 className="text-base font-semibold text-text-primary mb-3">
                  Add packages
                </h2>
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-text-tertiary" />
                  <input
                    ref={inputRef}
                    type="text"
                    className="w-full pl-9 pr-9 py-2 bg-surface-primary border border-border-default rounded-lg text-sm text-text-primary placeholder:text-text-tertiary transition-colors focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
                    placeholder="Search GitHub repositories..."
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                  />
                  {query && (
                    <button
                      onClick={() => setQuery('')}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-text-tertiary hover:text-text-secondary"
                    >
                      <X className="w-4 h-4" />
                    </button>
                  )}
                </div>
              </div>

              {/* Search Results */}
              {showSearchResults && (
                <div>
                  {isSearching ? (
                    <div className="flex items-center justify-center gap-2 py-8 text-text-secondary text-sm">
                      <Loader2 className="w-4 h-4 animate-spin" />
                      Searching...
                    </div>
                  ) : searchResults.length > 0 ? (
                    searchResults.map((result) => {
                      const key = `${result.owner}/${result.repo}`
                      return (
                        <SearchResultItem
                          key={key}
                          result={result}
                          isWatched={watchedRepos.has(key.toLowerCase())}
                          isAdding={addingGithub === key}
                          onAdd={() => handleAddFromGithub(result)}
                        />
                      )
                    })
                  ) : (
                    <div className="py-8 text-center text-sm text-text-secondary">
                      No repositories found for &ldquo;{debouncedQuery}&rdquo;
                    </div>
                  )}
                </div>
              )}

              {!showSearchResults && (
                <div className="py-8 text-center text-sm text-text-tertiary">
                  Type at least 2 characters to search
                </div>
              )}
            </Card>

            {/* Current Watchlist */}
            <Card padding="none">
              <div className="px-4 py-3 border-b border-border-default">
                <h2 className="text-base font-semibold text-text-primary">
                  Your watchlist
                </h2>
                {watchlist && (
                  <p className="text-xs text-text-tertiary mt-0.5">
                    {watchlist.length} package
                    {watchlist.length !== 1 ? 's' : ''}
                  </p>
                )}
              </div>

              {watchlistLoading ? (
                <div className="py-8 text-center text-sm text-text-secondary">
                  Loading watchlist...
                </div>
              ) : (watchlist ?? []).length === 0 ? (
                <div className="py-8 text-center">
                  <p className="text-sm text-text-secondary">
                    Your watchlist is empty. Search above to add packages.
                  </p>
                </div>
              ) : (
                (watchlist ?? []).map((pkg) => (
                  <WatchedPackageItem
                    key={pkg.id}
                    pkg={pkg}
                    onRemove={() => handleRemove(pkg.id)}
                    isRemoving={false}
                  />
                ))
              )}
            </Card>
          </div>
        </Container>
      </main>
    </div>
  )
}
