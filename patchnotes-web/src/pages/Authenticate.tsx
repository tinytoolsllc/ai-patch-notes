import { useStytch, useStytchUser } from '@stytch/react'
import { useNavigate } from '@tanstack/react-router'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Container } from '../components/ui'
import { api } from '../api/client'

export function Authenticate() {
  const stytch = useStytch()
  const { user, isInitialized } = useStytchUser()
  const navigate = useNavigate()
  const hasAuthenticated = useRef(false)

  const { token, tokenType, returnUrl } = useMemo(() => {
    const params = new URLSearchParams(window.location.search)
    // Check localStorage first, then fall back to query param for backwards compat
    const stored = localStorage.getItem('stytch_return_url')
    localStorage.removeItem('stytch_return_url')
    const raw = stored ?? params.get('returnUrl')
    return {
      token: params.get('token'),
      tokenType: params.get('stytch_token_type'),
      // Only allow relative paths to prevent open redirects
      returnUrl: raw?.startsWith('/') ? raw : '/',
    }
  }, [])

  const [error, setError] = useState<string | null>(
    !token || !tokenType ? 'Invalid authentication link' : null
  )

  useEffect(
    function authenticateWithToken() {
      if (!isInitialized || error) return

      if (user) {
        navigate({ to: returnUrl })
        return
      }

      if (hasAuthenticated.current) return
      hasAuthenticated.current = true

      const authenticate = async () => {
        if (!token) return // TypeScript guard - error state already handles this
        try {
          if (tokenType === 'magic_links') {
            await stytch.magicLinks.authenticate(token, {
              session_duration_minutes: 43200, // 30 days
            })
          } else if (tokenType === 'oauth') {
            await stytch.oauth.authenticate(token, {
              session_duration_minutes: 43200, // 30 days
            })
          } else {
            setError(`Unknown token type: ${tokenType}`)
            return
          }

          // Sync user to backend database
          const loginResponse = await api.post<{ isNewUser?: boolean }>(
            '/users/login'
          )
          navigate({ to: loginResponse?.isNewUser ? '/onboarding' : returnUrl })
        } catch (err) {
          console.error('Authentication failed:', err)
          setError('Authentication failed. Please try again.')
        }
      }

      authenticate()
    },
    [stytch, token, tokenType, user, isInitialized, navigate, error, returnUrl]
  )

  if (error) {
    return (
      <Container>
        <div className="flex min-h-screen flex-col items-center justify-center">
          <div className="text-center">
            <h1 className="text-2xl font-bold text-major">
              Authentication Error
            </h1>
            <p className="mt-2 text-text-secondary">{error}</p>
            <button
              onClick={() =>
                navigate({ to: '/login', search: { returnUrl: undefined } })
              }
              className="mt-4 rounded-md bg-brand-600 px-4 py-2 text-white hover:bg-brand-700"
            >
              Back to Login
            </button>
          </div>
        </div>
      </Container>
    )
  }

  return (
    <Container>
      <div className="flex min-h-screen flex-col items-center justify-center">
        <div className="text-center">
          <h1 className="text-2xl font-bold text-text-primary">
            Authenticating...
          </h1>
          <p className="mt-2 text-text-secondary">
            Please wait while we verify your credentials.
          </p>
        </div>
      </div>
    </Container>
  )
}
