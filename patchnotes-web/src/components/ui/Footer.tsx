import { Hammer, Anvil } from 'lucide-react'
import { Link } from '@tanstack/react-router'
import { Container } from './Container'

export function Footer() {
  return (
    <footer className="mt-auto bg-surface-primary/80 backdrop-blur-md border-t border-border-default">
      <Container>
        <div className="pt-1.5 pb-3 flex flex-col gap-0.5">
          {/* Top: Brand + Nav */}
          <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-0.5">
            {/* Left: Brand */}
            <p className="text-sm">
              <span className="font-semibold text-text-primary">
                My Release Notes
              </span>{' '}
              <span className="text-xs text-text-tertiary">
                by{' '}
                <a
                  href="https://www.yourtinytools.com"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-text-secondary hover:text-text-primary transition-colors"
                >
                  Tiny Tools
                </a>
              </span>
            </p>

            {/* Right: Nav links */}
            <nav className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-text-secondary">
              <Link to="/" className="hover:text-text-primary transition-colors">
                Home
              </Link>
              <Link
                to="/pricing"
                className="hover:text-text-primary transition-colors"
              >
                Pricing
              </Link>
              <Link
                to="/about"
                className="hover:text-text-primary transition-colors"
              >
                About
              </Link>
              <Link
                to="/privacy"
                className="hover:text-text-primary transition-colors"
              >
                Privacy
              </Link>
            </nav>
          </div>

          {/* Bottom: Tagline + Copyright + Trademark */}
          <div className="border-t border-border-muted pt-2 flex flex-col gap-0.5">
            <div className="flex items-center justify-center gap-1.5">
              <Hammer
                className="w-4 h-4 text-text-tertiary"
                strokeWidth={1.5}
              />
              <span className="text-sm text-text-secondary">
                Forged in Gas Town
              </span>
              <Anvil className="w-4 h-4 text-text-tertiary" strokeWidth={1.5} />
              <span className="text-xs text-text-tertiary">
                &middot; &copy; 2026 My Release Notes &middot; A{' '}
                <a
                  href="https://www.yourtinytools.com"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="hover:text-text-secondary transition-colors"
                >
                  Tiny Tools
                </a>{' '}
                product
              </span>
            </div>
            <p className="text-xs text-text-tertiary">
              GitHub is a trademark of GitHub, Inc. This site is not affiliated
              with GitHub, Inc.
            </p>
          </div>
        </div>
      </Container>
    </footer>
  )
}
