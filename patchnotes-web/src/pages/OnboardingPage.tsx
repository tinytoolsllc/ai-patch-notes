import { useEffect, useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useStytchUser } from '@stytch/react'
import { Loader2, Code2, Server, TrendingUp, CircleDashed } from 'lucide-react'
import { Container, Card } from '../components/ui'
import { useWatchlistTemplates, useApplyTemplate } from '../api/hooks'

const templateIcons: Record<string, typeof Code2> = {
  Frontend: Code2,
  Backend: Server,
  Popular: TrendingUp,
  Empty: CircleDashed,
}

export function OnboardingPage() {
  const { user, isInitialized } = useStytchUser()
  const navigate = useNavigate()
  const { data: templates, isLoading } = useWatchlistTemplates()
  const applyTemplate = useApplyTemplate()
  const [applying, setApplying] = useState<string | null>(null)

  useEffect(() => {
    if (isInitialized && !user) {
      navigate({ to: '/login' })
    }
  }, [isInitialized, user, navigate])

  if (!isInitialized || !user) {
    return (
      <div className="min-h-screen bg-surface-secondary flex items-center justify-center">
        <p className="text-text-secondary">Loading...</p>
      </div>
    )
  }

  const handleSelect = async (template: {
    name: string
    packages?: string[]
  }) => {
    setApplying(template.name)
    try {
      if ((template.packages ?? []).length > 0) {
        await applyTemplate.mutateAsync(template.name)
      }
      navigate({ to: '/watchlist' })
    } catch {
      navigate({ to: '/watchlist' })
    }
  }

  return (
    <div className="min-h-screen bg-surface-secondary">
      <main className="flex flex-col items-center justify-center px-4 py-16 min-h-screen">
        <Container>
          <div className="max-w-2xl mx-auto text-center mb-10">
            <h1 className="text-3xl font-bold text-text-primary">
              Welcome to My Release Notes
            </h1>
            <p className="mt-3 text-text-secondary text-lg">
              Pick a template to start your watchlist, or start from scratch.
            </p>
          </div>

          <div className="max-w-2xl mx-auto grid grid-cols-1 sm:grid-cols-2 gap-4">
            {isLoading ? (
              <div className="col-span-full flex items-center justify-center py-12">
                <Loader2 className="w-6 h-6 animate-spin text-text-tertiary" />
              </div>
            ) : (
              (templates ?? []).map((template) => {
                const Icon = templateIcons[template.name] ?? CircleDashed
                const isApplying = applying === template.name
                const isDisabled = applying !== null

                return (
                  <button
                    key={template.name}
                    onClick={() => handleSelect(template)}
                    disabled={isDisabled}
                    className="text-left"
                  >
                    <Card
                      className={`h-full transition-all cursor-pointer ${
                        isDisabled && !isApplying
                          ? 'opacity-50'
                          : 'hover:border-brand-500 hover:shadow-md'
                      }`}
                    >
                      <div className="flex items-start gap-3">
                        <div className="flex-shrink-0 w-10 h-10 rounded-lg bg-surface-tertiary flex items-center justify-center">
                          {isApplying ? (
                            <Loader2 className="w-5 h-5 animate-spin text-brand-600" />
                          ) : (
                            <Icon className="w-5 h-5 text-text-secondary" />
                          )}
                        </div>
                        <div>
                          <h2 className="font-semibold text-text-primary">
                            {template.name}
                          </h2>
                          <p className="text-sm text-text-secondary mt-0.5">
                            {template.description}
                          </p>
                          {(template.packages ?? []).length > 0 && (
                            <p className="text-xs text-text-tertiary mt-2">
                              {(template.packages ?? []).length} package
                              {(template.packages ?? []).length !== 1
                                ? 's'
                                : ''}
                            </p>
                          )}
                        </div>
                      </div>
                    </Card>
                  </button>
                )
              })
            )}
          </div>

          <div className="max-w-2xl mx-auto mt-6 text-center">
            <button
              onClick={() => navigate({ to: '/watchlist' })}
              disabled={applying !== null}
              className="text-sm text-text-tertiary hover:text-text-secondary transition-colors disabled:opacity-50"
            >
              Skip for now
            </button>
          </div>
        </Container>
      </main>
    </div>
  )
}
