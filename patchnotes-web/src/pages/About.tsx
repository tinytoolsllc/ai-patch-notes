import { Link } from '@tanstack/react-router'
import { Header, HeaderTitle, Container } from '../components/ui'
import { ThemeToggle } from '../components/theme'
import { UserMenu } from '../components/auth'
import { Logo } from '../components/landing/Logo'

export function About() {
  return (
    <div className="min-h-screen bg-surface-secondary">
      <Header
        breadcrumb={
          <>
            <Link to="/" className="hover:text-text-primary transition-colors">
              Home
            </Link>
            <span className="text-text-tertiary">/</span>
            <span className="text-text-primary">About</span>
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

      <main className="py-12">
        <Container>
          <div className="max-w-3xl mx-auto prose prose-neutral dark:prose-invert">
            <h1 className="text-3xl font-bold text-text-primary mb-6">
              My Release Notes
            </h1>

            <p className="text-text-secondary leading-relaxed">
              My Release Notes is the easiest way to stay on top of GitHub
              releases for the packages you depend on. Whether you maintain a
              handful of projects or hundreds, we surface the updates that
              matter so you never miss a breaking change, security patch, or
              exciting new feature.
            </p>

            <section className="mt-10">
              <h2 className="text-xl font-semibold text-text-primary mb-4">
                A Tiny Tools Product
              </h2>
              <p className="text-text-secondary leading-relaxed">
                My Release Notes is built and maintained by{' '}
                <a
                  href="https://www.yourtinytools.com"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-brand-500 hover:underline"
                >
                  Tiny Tools
                </a>
                , a small studio focused on crafting simple, sharp digital
                utilities for individual performers and small but mighty teams.
                We believe the best tools do one thing well and stay out of your
                way.
              </p>
            </section>

            <section className="mt-10">
              <h2 className="text-xl font-semibold text-text-primary mb-4">
                Who It's For
              </h2>
              <ul className="list-disc pl-6 space-y-2 text-text-secondary">
                <li>
                  <strong>Application developers</strong> who want to know when
                  their dependencies ship updates
                </li>
                <li>
                  <strong>DevOps &amp; platform teams</strong> tracking
                  toolchain releases across many repos
                </li>
                <li>
                  <strong>Open-source maintainers</strong> keeping tabs on the
                  ecosystem around their projects
                </li>
                <li>
                  <strong>Anyone curious</strong> about what's new in the
                  packages they use every day
                </li>
              </ul>
            </section>

            <section className="mt-10">
              <h2 className="text-xl font-semibold text-text-primary mb-4">
                Forged in Gas Town
              </h2>
              <p className="text-text-secondary leading-relaxed">
                My Release Notes was forged in{' '}
                <a
                  href="https://github.com/steveyegge/gastown"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-brand-500 hover:underline"
                >
                  Gas Town
                </a>
                , a multi-agent workspace manager. Good tools solve real
                problems, and My Release Notes was born out of our own
                frustration with missing important releases buried in noisy
                GitHub feeds.
              </p>
            </section>
          </div>
        </Container>
      </main>
    </div>
  )
}
