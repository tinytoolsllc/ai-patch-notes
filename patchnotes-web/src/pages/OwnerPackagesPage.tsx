import { Link } from '@tanstack/react-router'
import { Header, HeaderTitle, Container, Card } from '../components/ui'
import { ThemeToggle } from '../components/theme'
import { UserMenu } from '../components/auth'
import { Logo } from '../components/landing/Logo'
import { usePackagesByOwner } from '../api/hooks'
import { formatDate } from '../utils/dateFormat'

interface OwnerPackagesPageProps {
  owner: string
}

export function OwnerPackagesPage({ owner }: OwnerPackagesPageProps) {
  const { data: packages, isLoading, error } = usePackagesByOwner(owner)

  return (
    <div className="min-h-screen bg-surface-secondary">
      <Header
        breadcrumb={
          <>
            <Link to="/" className="hover:text-text-primary transition-colors">
              Home
            </Link>
            <span className="text-text-tertiary">/</span>
            <span className="text-text-primary">{owner}</span>
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
          <h2 className="text-xl font-semibold text-text-primary mb-6">
            Packages by {owner}
          </h2>

          {isLoading && (
            <div className="space-y-4">
              {[1, 2, 3].map((i) => (
                <Card key={i} className="animate-pulse">
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-lg bg-surface-tertiary" />
                    <div className="flex-1">
                      <div className="h-5 w-40 bg-surface-tertiary rounded mb-2" />
                      <div className="h-4 w-24 bg-surface-tertiary rounded" />
                    </div>
                  </div>
                </Card>
              ))}
            </div>
          )}

          {!!error && (
            <p className="text-text-secondary">
              Failed to load packages for this owner.
            </p>
          )}

          {!isLoading && packages && packages.length === 0 && (
            <p className="text-text-secondary">
              No packages found for {owner}.
            </p>
          )}

          {packages && packages.length > 0 && (
            <div className="space-y-4">
              {packages.map((pkg) => (
                <Link
                  key={pkg.id}
                  to="/packages/$owner/$repo"
                  params={{ owner: pkg.githubOwner, repo: pkg.githubRepo }}
                  className="block"
                >
                  <Card className="hover:shadow-md transition-shadow cursor-pointer">
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 rounded-lg bg-brand-100 dark:bg-brand-900/30 flex items-center justify-center font-semibold text-lg text-brand-600 dark:text-brand-400">
                        {(pkg.npmName ?? pkg.name).charAt(0).toUpperCase()}
                      </div>
                      <div className="flex-1 min-w-0">
                        <h3 className="font-semibold text-text-primary truncate">
                          {pkg.npmName ?? pkg.name}
                        </h3>
                        <p className="text-sm text-text-secondary">
                          {pkg.githubOwner}/{pkg.githubRepo}
                        </p>
                      </div>
                      <div className="text-right flex-shrink-0">
                        {pkg.latestVersion && (
                          <span className="text-sm font-mono text-text-secondary">
                            {pkg.latestVersion}
                          </span>
                        )}
                        {pkg.lastUpdated && (
                          <p className="text-xs text-text-tertiary mt-0.5">
                            {formatDate(pkg.lastUpdated)}
                          </p>
                        )}
                      </div>
                    </div>
                  </Card>
                </Link>
              ))}
            </div>
          )}
        </Container>
      </main>
    </div>
  )
}
