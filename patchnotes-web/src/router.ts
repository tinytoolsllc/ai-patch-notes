import { createRouter } from '@tanstack/react-router'
import { routeTree } from './routeTree.gen'
import { queryClient } from './queryClient'
import { PendingSpinner } from './components/ui/PendingSpinner'

export const router = createRouter({
  routeTree,
  context: { queryClient },
  defaultPendingMs: 200,
  defaultPendingMinMs: 300,
  defaultPendingComponent: PendingSpinner,
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
