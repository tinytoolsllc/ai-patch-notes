import { useRouterState } from '@tanstack/react-router'

export function NavigationProgress() {
  const isLoading = useRouterState({ select: (s) => s.isLoading })

  if (!isLoading) return null

  return (
    <div className="fixed top-0 left-0 right-0 z-50 h-0.5">
      <div className="h-full bg-primary animate-progress origin-left" />
    </div>
  )
}
