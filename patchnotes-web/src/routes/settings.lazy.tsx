import { createLazyFileRoute } from '@tanstack/react-router'
import { Settings } from '../pages/Settings'

export const Route = createLazyFileRoute('/settings')({
  component: Settings,
})
