import { createFileRoute } from '@tanstack/react-router'
import { Login } from '../pages/Login'
import { seoHead } from '../seo'

export const Route = createFileRoute('/login')({
  component: Login,
  validateSearch: (search: Record<string, unknown>) => ({
    returnUrl:
      typeof search.returnUrl === 'string' ? search.returnUrl : undefined,
  }),
  head: () => ({
    ...seoHead({
      title: 'Sign In | My Release Notes',
      description:
        'Sign in to My Release Notes to manage your watchlist and track GitHub releases.',
      path: '/login',
      noindex: true,
    }),
  }),
})
