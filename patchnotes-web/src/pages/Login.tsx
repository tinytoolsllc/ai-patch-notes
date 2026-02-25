import { StytchLogin } from '@stytch/react'
import { useStytchUser } from '@stytch/react'
import { Link, useNavigate } from '@tanstack/react-router'
import { useEffect, useMemo } from 'react'
import { ArrowLeft } from 'lucide-react'
import { stytchLoginConfig, getStytchPresentation } from '../auth/stytch'
import { useTheme } from '../components/theme'
import { ThemeToggle } from '../components/theme'

export function Login() {
  const { user, isInitialized } = useStytchUser()
  const navigate = useNavigate()
  const { resolvedTheme } = useTheme()

  const stytchPresentation = useMemo(
    () => getStytchPresentation(resolvedTheme === 'dark'),
    [resolvedTheme]
  )

  useEffect(() => {
    if (isInitialized && user) {
      navigate({ to: '/' })
    }
  }, [user, isInitialized, navigate])

  if (!isInitialized) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-surface-secondary">
        <div className="animate-pulse text-text-tertiary">Loading...</div>
      </div>
    )
  }

  if (user) {
    return null
  }

  return (
    <div className="min-h-screen bg-surface-secondary">
      {/* Subtle gradient overlay */}
      <div
        className="fixed inset-0 pointer-events-none opacity-40 dark:opacity-20"
        style={{
          background:
            resolvedTheme === 'dark'
              ? 'radial-gradient(ellipse at top, rgba(99, 102, 241, 0.15) 0%, transparent 50%)'
              : 'radial-gradient(ellipse at top, rgba(79, 70, 229, 0.08) 0%, transparent 50%)',
        }}
      />

      {/* Header */}
      <header className="sticky top-0 z-10 flex items-center justify-between px-6 py-4">
        <Link
          to="/"
          className="flex items-center gap-2 text-text-secondary hover:text-text-primary transition-colors"
        >
          <ArrowLeft className="w-4 h-4" />
          <span className="text-sm font-medium">Back</span>
        </Link>
        <ThemeToggle />
      </header>

      {/* Main content */}
      <main className="relative z-10 flex flex-col items-center justify-center px-4 pt-24 pb-24">
        <StytchLogin
          config={stytchLoginConfig}
          presentation={stytchPresentation}
        />

        {/* Footer text */}
        <p className="mt-6 text-center text-xs text-text-tertiary">
          By continuing, you agree to our terms of service
        </p>
      </main>
    </div>
  )
}
