import { useState, useEffect } from 'react'
import { Link, useNavigate } from '@tanstack/react-router'
import { useStytchUser } from '@stytch/react'
import { AppHeader, Container, Button, Input } from '../components/ui'
import { Checkbox } from '../components/ui/Checkbox'
import {
  useGetCurrentUser,
  useUpdateCurrentUser,
  useGetEmailPreferences,
  useUpdateEmailPreferences,
  getGetEmailPreferencesQueryKey,
  getGetCurrentUserQueryKey,
} from '../api/hooks'
import { useQueryClient } from '@tanstack/react-query'

const DAY_NAMES = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
]

function formatHour(hour: number): string {
  const ampm = hour < 12 ? 'AM' : 'PM'
  const h = hour % 12 === 0 ? 12 : hour % 12
  return `${h}:00 ${ampm}`
}

function utcHourToLocal(utcHour: number): number {
  const now = new Date()
  now.setUTCHours(utcHour, 0, 0, 0)
  return now.getHours()
}

function localHourToUtc(localHour: number): number {
  const now = new Date()
  now.setHours(localHour, 0, 0, 0)
  return now.getUTCHours()
}

function getLocalTimezoneAbbr(): string {
  return (
    new Intl.DateTimeFormat('en', { timeZoneName: 'short' })
      .formatToParts(new Date())
      .find((p) => p.type === 'timeZoneName')?.value ?? 'local'
  )
}

function SettingsSkeleton() {
  return (
    <div className="animate-pulse">
      <div className="space-y-6">
        <div>
          <div className="h-4 w-24 bg-surface-tertiary rounded mb-2" />
          <div className="h-10 w-full bg-surface-tertiary rounded-lg" />
        </div>
        <div className="h-9 w-16 bg-surface-tertiary rounded-lg" />
      </div>

      <div className="mt-10 pt-8 border-t border-border-default">
        <div className="h-5 w-44 bg-surface-tertiary rounded mb-4" />
        <div className="space-y-4">
          <div className="flex items-center gap-3">
            <div className="w-5 h-5 bg-surface-tertiary rounded" />
            <div>
              <div className="h-4 w-36 bg-surface-tertiary rounded mb-1" />
              <div className="h-3 w-64 bg-surface-tertiary rounded" />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <div className="h-4 w-8 bg-surface-tertiary rounded mb-1.5" />
              <div className="h-10 w-full bg-surface-tertiary rounded-lg" />
            </div>
            <div>
              <div className="h-4 w-16 bg-surface-tertiary rounded mb-1.5" />
              <div className="h-10 w-full bg-surface-tertiary rounded-lg" />
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

export function Settings() {
  const { user, isInitialized } = useStytchUser()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const {
    data: currentUser,
    error: currentUserError,
    isLoading: userLoading,
  } = useGetCurrentUser({
    query: { enabled: !!user },
  })
  const updateUser = useUpdateCurrentUser()

  const userData = currentUser?.status === 200 ? currentUser.data : null
  const serverName = userData?.name ?? ''
  const [nameOverride, setNameOverride] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const name = nameOverride ?? serverName

  // Redirect if not authenticated
  useEffect(() => {
    if (isInitialized && !user) {
      navigate({ to: '/login', search: { returnUrl: '/settings' } })
    }
  }, [isInitialized, user, navigate])

  const handleSave = () => {
    setSaved(false)

    const previousData = queryClient.getQueryData(getGetCurrentUserQueryKey())

    // Optimistically update cache
    queryClient.setQueryData(
      getGetCurrentUserQueryKey(),
      (old: typeof currentUser) => {
        if (!old || old.status !== 200) return old
        return {
          ...old,
          data: { ...old.data, name: name || null },
        }
      }
    )

    setNameOverride(null)

    updateUser.mutate(
      { data: { name: name || null } },
      {
        onSuccess: () => {
          setSaved(true)
          queryClient.invalidateQueries({
            queryKey: getGetCurrentUserQueryKey(),
          })
        },
        onError: () => {
          queryClient.setQueryData(getGetCurrentUserQueryKey(), previousData)
        },
      }
    )
  }

  // Email preferences
  const isPro = userData?.isPro ?? false

  const {
    data: emailPrefs,
    error: emailPrefsError,
    isLoading: emailPrefsLoading,
  } = useGetEmailPreferences({
    query: { enabled: !!user },
  })
  const updateEmailPrefs = useUpdateEmailPreferences()

  const emailPrefsData = emailPrefs?.status === 200 ? emailPrefs.data : null
  const serverDigestEnabled = emailPrefsData?.emailDigestEnabled ?? true
  const serverDigestDay = emailPrefsData?.digestDay ?? 1
  const serverDigestHour = emailPrefsData?.digestHour ?? 4

  // Local overrides — only set while the user is editing (before saving)
  const [digestEnabledOverride, setDigestEnabledOverride] = useState<
    boolean | null
  >(null)
  const [digestDayOverride, setDigestDayOverride] = useState<number | null>(
    null
  )
  const [digestHourOverride, setDigestHourOverride] = useState<number | null>(
    null
  )
  const [emailPrefsSaved, setEmailPrefsSaved] = useState(false)

  // Displayed values: local override if editing, otherwise server data
  const digestEnabled = digestEnabledOverride ?? serverDigestEnabled
  const digestDay = digestDayOverride ?? serverDigestDay
  const digestHour =
    digestHourOverride !== null
      ? digestHourOverride
      : utcHourToLocal(serverDigestHour)

  const handleSaveEmailPrefs = () => {
    setEmailPrefsSaved(false)

    const newData = {
      emailDigestEnabled: digestEnabled,
      digestDay,
      digestHour: localHourToUtc(digestHour),
    }

    // Capture previous cache for rollback
    const previousData = queryClient.getQueryData(
      getGetEmailPreferencesQueryKey()
    )

    // Optimistically update the cache so UI reflects changes immediately
    queryClient.setQueryData(
      getGetEmailPreferencesQueryKey(),
      (old: typeof emailPrefs) => {
        if (!old || old.status !== 200) return old
        return {
          ...old,
          data: {
            ...old.data,
            emailDigestEnabled: newData.emailDigestEnabled,
            digestDay: newData.digestDay,
            digestHour: newData.digestHour,
          },
        }
      }
    )

    // Clear overrides since cache now has the new values
    setDigestEnabledOverride(null)
    setDigestDayOverride(null)
    setDigestHourOverride(null)

    updateEmailPrefs.mutate(
      { data: newData },
      {
        onSuccess: () => {
          setEmailPrefsSaved(true)
          queryClient.invalidateQueries({
            queryKey: getGetEmailPreferencesQueryKey(),
          })
        },
        onError: () => {
          // Rollback cache on failure
          queryClient.setQueryData(
            getGetEmailPreferencesQueryKey(),
            previousData
          )
        },
      }
    )
  }

  const isLoading = userLoading || emailPrefsLoading
  const tzAbbr = getLocalTimezoneAbbr()
  const scheduleText = `Your digest will be sent every ${DAY_NAMES[digestDay]} at ${formatHour(digestHour)} ${tzAbbr}`

  if (!isInitialized || !user) {
    return null
  }

  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader
        breadcrumbs={<span className="text-text-primary">Settings</span>}
      />

      <main className="py-12">
        <Container>
          <div className="max-w-lg mx-auto">
            <h1 className="text-2xl font-bold text-text-primary mb-8">
              Settings
            </h1>

            {isLoading ? (
              <SettingsSkeleton />
            ) : (
              <>
                <div className="space-y-6">
                  <Input
                    label="Display Name"
                    type="text"
                    value={name}
                    onChange={(e) => {
                      setNameOverride(e.target.value)
                      setSaved(false)
                    }}
                    placeholder="Your name"
                  />

                  {currentUserError != null && (
                    <p className="text-sm text-red-600 dark:text-red-400">
                      Failed to load profile
                    </p>
                  )}

                  <div className="flex items-center gap-3">
                    <Button
                      onClick={handleSave}
                      disabled={updateUser.isPending}
                    >
                      {updateUser.isPending ? 'Saving...' : 'Save'}
                    </Button>
                    {saved && (
                      <span className="text-sm text-emerald-600 dark:text-emerald-400">
                        Saved successfully
                      </span>
                    )}
                    {updateUser.isError && (
                      <span className="text-sm text-red-600 dark:text-red-400">
                        Failed to save
                      </span>
                    )}
                  </div>
                </div>

                <div className="mt-10 pt-8 border-t border-border-default">
                  <h2 className="text-lg font-semibold text-text-primary mb-4">
                    Email Digest Schedule
                  </h2>

                  {emailPrefsError != null && (
                    <p className="text-sm text-red-600 dark:text-red-400 mb-4">
                      Failed to load email preferences
                    </p>
                  )}

                  <div className={`space-y-4 ${!isPro ? 'opacity-60' : ''}`}>
                    <Checkbox
                      label="Enable email digest"
                      description="Receive a weekly digest of release notes in your inbox"
                      checked={digestEnabled}
                      onChange={(e) => {
                        setDigestEnabledOverride(e.target.checked)
                        setEmailPrefsSaved(false)
                      }}
                      disabled={!isPro}
                    />

                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <label className="block text-sm font-medium text-text-primary mb-1.5">
                          Day
                        </label>
                        <select
                          value={digestDay}
                          onChange={(e) => {
                            setDigestDayOverride(Number(e.target.value))
                            setEmailPrefsSaved(false)
                          }}
                          disabled={!isPro}
                          className="w-full px-3 py-2 text-sm rounded-lg border border-border-default bg-surface-primary text-text-primary focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                          {DAY_NAMES.map((day, i) => (
                            <option key={day} value={i}>
                              {day}
                            </option>
                          ))}
                        </select>
                      </div>

                      <div>
                        <label className="block text-sm font-medium text-text-primary mb-1.5">
                          Hour ({tzAbbr})
                        </label>
                        <select
                          value={digestHour}
                          onChange={(e) => {
                            setDigestHourOverride(Number(e.target.value))
                            setEmailPrefsSaved(false)
                          }}
                          disabled={!isPro}
                          className="w-full px-3 py-2 text-sm rounded-lg border border-border-default bg-surface-primary text-text-primary focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                          {Array.from({ length: 24 }, (_, i) => (
                            <option key={i} value={i}>
                              {formatHour(i)}
                            </option>
                          ))}
                        </select>
                      </div>
                    </div>

                    {isPro && (
                      <p className="text-sm text-text-secondary">
                        {scheduleText}
                      </p>
                    )}
                  </div>

                  {!isPro && (
                    <div className="mt-3 p-3 rounded-lg bg-surface-primary border border-border-default">
                      <p className="text-sm text-text-secondary">
                        <Link
                          to="/pricing"
                          className="font-medium text-brand-600 hover:text-brand-700 underline"
                        >
                          Upgrade to Pro
                        </Link>{' '}
                        to customize your digest schedule
                      </p>
                    </div>
                  )}

                  {isPro && (
                    <div className="mt-4 flex items-center gap-3">
                      <Button
                        onClick={handleSaveEmailPrefs}
                        disabled={updateEmailPrefs.isPending}
                      >
                        {updateEmailPrefs.isPending
                          ? 'Saving...'
                          : 'Save Schedule'}
                      </Button>
                      {emailPrefsSaved && (
                        <span className="text-sm text-emerald-600 dark:text-emerald-400">
                          Saved successfully
                        </span>
                      )}
                      {updateEmailPrefs.isError && (
                        <span className="text-sm text-red-600 dark:text-red-400">
                          Failed to save
                        </span>
                      )}
                    </div>
                  )}
                </div>
              </>
            )}
          </div>
        </Container>
      </main>
    </div>
  )
}
