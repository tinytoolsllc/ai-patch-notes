import { createLazyFileRoute } from '@tanstack/react-router'
import { AdminReset } from '../pages/AdminReset'

export const Route = createLazyFileRoute('/admin_/reset')({
  component: AdminReset,
})
