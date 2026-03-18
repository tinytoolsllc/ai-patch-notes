import { createLazyFileRoute } from '@tanstack/react-router'
import { Pricing } from '../pages/Pricing'

export const Route = createLazyFileRoute('/pricing')({
  component: Pricing,
})
