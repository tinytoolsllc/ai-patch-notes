import { createFileRoute } from '@tanstack/react-router'
import { OnboardingPage } from '../pages/OnboardingPage'
import { seoHead } from '../seo'

export const Route = createFileRoute('/onboarding')({
  component: OnboardingPage,
  head: () => ({
    ...seoHead({
      title: 'Get Started | My Release Notes',
      description: 'Choose a watchlist template to get started.',
      path: '/onboarding',
      noindex: true,
    }),
  }),
})
