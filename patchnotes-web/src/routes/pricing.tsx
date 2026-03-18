import { createFileRoute } from '@tanstack/react-router'
import { seoHead } from '../seo'

export const Route = createFileRoute('/pricing')({
  head: () => ({
    ...seoHead({
      title: 'Pricing | My Release Notes',
      description:
        'Simple, transparent pricing for My Release Notes. Free tier included. Track GitHub releases with AI-powered summaries.',
      path: '/pricing',
    }),
  }),
})
