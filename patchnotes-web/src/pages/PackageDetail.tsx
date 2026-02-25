import { Link, useNavigate } from '@tanstack/react-router'
import {
  AppHeader,
  Container,
  Card,
  CardHeader,
  CardTitle,
  CardContent,
  Badge,
} from '../components/ui'
import { ReleaseCard } from '../components/releases'
import { usePackage, usePackageReleases } from '../api/hooks'
import type { PackageReleaseDto } from '../api/generated/model'
import { formatDateTime } from '../utils/dateFormat'

interface PackageDetailProps {
  packageId: string
}

function getReleaseUrl(release: PackageReleaseDto): string {
  const { githubOwner, githubRepo } = release.package
  return `https://github.com/${githubOwner}/${githubRepo}/releases/tag/${release.tag}`
}

export function PackageDetail({ packageId }: PackageDetailProps) {
  const navigate = useNavigate()
  const {
    data: pkg,
    isLoading: packageLoading,
    error: packageError,
  } = usePackage(packageId)
  const { data: releases, isLoading: releasesLoading } =
    usePackageReleases(packageId)

  if (packageLoading) {
    return (
      <div className="min-h-screen bg-surface-secondary">
        <AppHeader />
        <main className="py-8">
          <Container>
            <p className="text-text-secondary">Loading package details...</p>
          </Container>
        </main>
      </div>
    )
  }

  if (packageError || !pkg) {
    return (
      <div className="min-h-screen bg-surface-secondary">
        <AppHeader />
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

  const githubUrl = `https://github.com/${pkg.githubOwner}/${pkg.githubRepo}`
  const npmUrl = `https://www.npmjs.com/package/${pkg.npmName}`

  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader
        breadcrumbs={
          <>
            <Link
              to="/packages/$owner"
              params={{ owner: pkg.githubOwner }}
              className="hover:text-text-primary transition-colors"
            >
              {pkg.githubOwner}
            </Link>
            <span className="text-text-tertiary">/</span>
            <span className="text-text-primary">{pkg.githubRepo}</span>
          </>
        }
      />

      <main className="py-8">
        <Container>
          {/* Package Info Card */}
          <Card className="mb-8">
            <CardHeader>
              <div className="flex items-center gap-3">
                <div className="w-12 h-12 rounded-lg bg-surface-tertiary flex items-center justify-center">
                  <svg
                    className="w-6 h-6 text-text-secondary"
                    viewBox="0 0 24 24"
                    fill="currentColor"
                  >
                    <path d="M20 3H4a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1zm-1 16H5V5h14v14z" />
                    <path d="M6 7h12v2H6zm0 4h12v2H6zm0 4h7v2H6z" />
                  </svg>
                </div>
                <div>
                  <CardTitle>{pkg.npmName}</CardTitle>
                  <p className="text-sm text-text-tertiary mt-0.5">
                    {pkg.githubOwner}/{pkg.githubRepo}
                  </p>
                </div>
              </div>
              {pkg.releaseCount != null && (
                <Badge>
                  {pkg.releaseCount} release{pkg.releaseCount !== 1 ? 's' : ''}
                </Badge>
              )}
            </CardHeader>

            <CardContent>
              <div className="flex flex-wrap items-center gap-6 text-sm">
                <a
                  href={githubUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-2 text-text-secondary hover:text-brand-600 transition-colors"
                >
                  <svg
                    className="w-5 h-5"
                    viewBox="0 0 24 24"
                    fill="currentColor"
                  >
                    <path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0 0 24 12c0-6.63-5.37-12-12-12z" />
                  </svg>
                  View on GitHub
                </a>
                <a
                  href={npmUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-2 text-text-secondary hover:text-brand-600 transition-colors"
                >
                  <svg
                    className="w-5 h-5"
                    viewBox="0 0 24 24"
                    fill="currentColor"
                  >
                    <path d="M0 7.334v8h6.666v1.332H12v-1.332h12v-8H0zm6.666 6.664H5.334v-4H3.999v4H1.335V8.667h5.331v5.331zm4 0v1.336H8.001V8.667h5.334v5.332h-2.669v-.001zm12.001 0h-1.33v-4h-1.336v4h-1.335v-4h-1.33v4h-2.671V8.667h8.002v5.331z" />
                  </svg>
                  View on npm
                </a>
                {pkg.lastFetchedAt && (
                  <span className="text-text-tertiary">
                    Last synced: {formatDateTime(pkg.lastFetchedAt)}
                  </span>
                )}
              </div>
            </CardContent>
          </Card>

          {/* Releases Section */}
          <section>
            <h2 className="text-xl font-semibold text-text-primary mb-4">
              Releases
            </h2>
            <div className="space-y-4">
              {releasesLoading ? (
                <p className="text-text-secondary">Loading releases...</p>
              ) : releases?.length === 0 ? (
                <p className="text-text-secondary">
                  No releases found for this package.
                </p>
              ) : (
                releases?.map((release) => (
                  <ReleaseCard
                    key={release.id}
                    tag={release.tag}
                    title={release.title}
                    body={release.body}
                    publishedAt={release.publishedAt}
                    htmlUrl={getReleaseUrl(release)}
                    onClick={() =>
                      navigate({
                        to: '/releases/$releaseId',
                        params: { releaseId: String(release.id) },
                      })
                    }
                  />
                ))
              )}
            </div>
          </section>
        </Container>
      </main>
    </div>
  )
}
