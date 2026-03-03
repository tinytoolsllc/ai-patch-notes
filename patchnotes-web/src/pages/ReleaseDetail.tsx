import { Link } from '@tanstack/react-router'
import { LazyMarkdown as Markdown } from '../components/LazyMarkdown'
import {
  AppHeader,
  Breadcrumb,
  BreadcrumbSeparator,
  Container,
  Card,
  CardHeader,
  CardTitle,
  CardContent,
} from '../components/ui'
import { VersionBadge } from '../components/releases'
import { useRelease } from '../api/hooks'
import {
  formatDateLong as formatDate,
  formatDateTime,
} from '../utils/dateFormat'

interface ReleaseDetailProps {
  releaseId: string
}

export function ReleaseDetail({ releaseId }: ReleaseDetailProps) {
  const { data: release, isLoading, error } = useRelease(releaseId)

  if (isLoading) {
    return (
      <div className="min-h-screen bg-surface-secondary">
        <AppHeader />
        <main className="py-8">
          <Container>
            <p className="text-text-secondary">Loading release details...</p>
          </Container>
        </main>
      </div>
    )
  }

  if (error || !release) {
    return (
      <div className="min-h-screen bg-surface-secondary">
        <AppHeader />
        <main className="py-8">
          <Container>
            <p className="text-text-secondary">
              The requested release could not be found.
            </p>
          </Container>
        </main>
      </div>
    )
  }

  const { githubOwner, githubRepo } = release.package
  const githubReleaseUrl = `https://github.com/${githubOwner}/${githubRepo}/releases/tag/${release.tag}`
  const displayTitle = release.title || release.tag

  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader
        breadcrumbs={
          <>
            <Breadcrumb to="/packages/$owner" params={{ owner: githubOwner }}>
              {githubOwner}
            </Breadcrumb>
            <BreadcrumbSeparator />
            <Breadcrumb
              to="/packages/$owner/$repo"
              params={{ owner: githubOwner, repo: githubRepo }}
            >
              {githubRepo}
            </Breadcrumb>
            <BreadcrumbSeparator />
            <Breadcrumb>{release.tag}</Breadcrumb>
          </>
        }
      />

      <main className="py-8">
        <Container>
          {/* Release Info Card */}
          <Card className="mb-8">
            <CardHeader>
              <div className="flex items-center gap-3">
                <VersionBadge version={release.tag} />
                <div>
                  <CardTitle>{displayTitle}</CardTitle>
                  {release.package.npmName && (
                    <p className="text-sm text-text-tertiary mt-0.5">
                      {release.package.npmName}
                    </p>
                  )}
                </div>
              </div>
              <time
                dateTime={release.publishedAt}
                className="text-sm text-text-tertiary"
              >
                {formatDate(release.publishedAt)}
              </time>
            </CardHeader>

            <CardContent>
              <div className="flex flex-wrap items-center gap-6 text-sm">
                <a
                  href={githubReleaseUrl}
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
                <Link
                  to="/packages/$owner/$repo"
                  params={{
                    owner: release.package.githubOwner,
                    repo: release.package.githubRepo,
                  }}
                  className="inline-flex items-center gap-2 text-text-secondary hover:text-brand-600 transition-colors"
                >
                  <svg
                    className="w-5 h-5"
                    viewBox="0 0 24 24"
                    fill="currentColor"
                  >
                    <path d="M20 3H4a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1zm-1 16H5V5h14v14z" />
                    <path d="M6 7h12v2H6zm0 4h12v2H6zm0 4h7v2H6z" />
                  </svg>
                  View Package
                </Link>
                <span className="text-text-tertiary">
                  Fetched: {formatDateTime(release.fetchedAt)}
                </span>
              </div>
            </CardContent>
          </Card>

          {/* Release Notes */}
          <section>
            <h2 className="text-xl font-semibold text-text-primary mb-4">
              Release Notes
            </h2>
            <Card>
              <CardContent className="mt-0">
                {release.body ? (
                  <div className="prose-release">
                    <Markdown>{release.body}</Markdown>
                  </div>
                ) : (
                  <p className="text-text-tertiary italic">
                    No release notes available.
                  </p>
                )}
              </CardContent>
            </Card>
          </section>
        </Container>
      </main>
    </div>
  )
}
