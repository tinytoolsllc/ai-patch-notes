import type { ReactNode } from 'react'
import { Link, type LinkProps } from '@tanstack/react-router'
import { List, Sparkles } from 'lucide-react'
import { useStytchUser } from '@stytch/react'
import { Container } from './Container'
import { Button } from './Button'
import { Logo } from '../landing/Logo'
import { UserMenu } from '../auth/UserMenu'
import { useSubscriptionStore } from '../../stores/subscriptionStore'

/** A single breadcrumb segment. Use `to` for link segments, omit for the current page. */
export function Breadcrumb({
  children,
  to,
  params,
}: {
  children: ReactNode
  to?: LinkProps['to']
  params?: LinkProps['params']
}) {
  if (to) {
    return (
      <Link
        to={to}
        params={params}
        className="hover:text-text-primary transition-colors"
      >
        {children}
      </Link>
    )
  }
  return <span className="font-semibold text-text-primary">{children}</span>
}

/** Separator between breadcrumb segments. */
export function BreadcrumbSeparator() {
  return <span className="text-text-tertiary">/</span>
}

interface AppHeaderProps {
  breadcrumbs?: ReactNode
}

export function AppHeader({ breadcrumbs }: AppHeaderProps) {
  const { user } = useStytchUser()
  const { isPro, startCheckout } = useSubscriptionStore()

  return (
    <header className="sticky top-0 z-30 bg-surface-primary/80 backdrop-blur-md border-b border-border-default">
      <Container>
        <div className="h-16 flex items-center justify-between">
          <div className="flex items-center gap-1.5">
            <Link
              to="/"
              className="flex items-center gap-2.5 hover:opacity-80 transition-opacity"
            >
              <Logo size={36} />
              <div>
                <p className="text-xl font-semibold text-text-primary">
                  My Release Notes
                </p>
                <p className="text-2xs text-text-tertiary leading-tight">
                  by Tiny Tools
                </p>
              </div>
            </Link>
            {breadcrumbs && (
              <div className="flex items-center gap-1.5 text-sm text-text-secondary">
                <span className="text-text-tertiary">/</span>
                {breadcrumbs}
              </div>
            )}
          </div>

          <div className="flex items-center gap-2">
            {user && (
              <Link
                to="/watchlist"
                className="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-text-secondary hover:text-text-primary transition-colors rounded-lg hover:bg-surface-secondary"
              >
                <List className="w-4 h-4" />
                Watchlist
              </Link>
            )}
            {user && !isPro && (
              <Button
                variant="primary"
                size="sm"
                onClick={startCheckout}
                className="flex items-center gap-1.5"
              >
                <Sparkles className="w-4 h-4" />
                Upgrade
              </Button>
            )}
            <UserMenu />
          </div>
        </div>
      </Container>
    </header>
  )
}
