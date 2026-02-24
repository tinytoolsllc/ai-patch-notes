import { useStytch, useStytchUser } from '@stytch/react'
import { useNavigate } from '@tanstack/react-router'
import { useEffect, useMemo, useState } from 'react'
import { Container } from '../components/ui'
import { api } from '../api/client'

export function ConfirmEmail() {
  const stytch = useStytch()
  const { user, isInitialized } = useStytchUser()
  const navigate = useNavigate()

  const token = useMemo(() => {
    return new URLSearchParams(window.location.search).get('token')
  }, [])

  const [error, setError] = useState<string | null>(
    !token ? 'Invalid email confirmation link' : null
  )
  const [done, setDone] = useState(false)

  useEffect(() => {
    if (!isInitialized || error || done) return

    // User must be logged in to change their email
    if (!user) {
      navigate({ to: '/login' })
      return
    }

    const confirm = async () => {
      if (!token) return

      try {
        // Authenticate the magic link token — this appends the new email to the Stytch user
        await stytch.magicLinks.authenticate(token, {
          session_duration_minutes: 43200, // 30 days
        })

        // Sync session email to our DB
        await api.post('/users/login')

        // Remove old email(s) from Stytch and update DB
        await api.post('/users/me/confirm-email-change')

        setDone(true)
      } catch (err) {
        console.error('Email confirmation failed:', err)
        setError('Email confirmation failed. The link may have expired.')
      }
    }

    confirm()
  }, [stytch, token, user, isInitialized, navigate, error, done])

  if (error) {
    return (
      <Container>
        <div className="flex min-h-screen flex-col items-center justify-center">
          <div className="text-center">
            <h1 className="text-2xl font-bold text-red-600">
              Email Change Failed
            </h1>
            <p className="mt-2 text-gray-600">{error}</p>
            <button
              onClick={() => navigate({ to: '/settings' })}
              className="mt-4 rounded-md bg-blue-600 px-4 py-2 text-white hover:bg-blue-700"
            >
              Back to Settings
            </button>
          </div>
        </div>
      </Container>
    )
  }

  if (done) {
    return (
      <Container>
        <div className="flex min-h-screen flex-col items-center justify-center">
          <div className="text-center">
            <h1 className="text-2xl font-bold text-text-primary">
              Email Updated
            </h1>
            <p className="mt-2 text-text-secondary">
              Your email address has been changed successfully.
            </p>
            <button
              onClick={() => navigate({ to: '/settings' })}
              className="mt-4 rounded-md bg-blue-600 px-4 py-2 text-white hover:bg-blue-700"
            >
              Back to Settings
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
            Confirming email change...
          </h1>
          <p className="mt-2 text-text-secondary">
            Please wait while we verify your new email address.
          </p>
        </div>
      </div>
    </Container>
  )
}
