import { render, screen, waitFor } from '../test/utils'
import { http, HttpResponse } from 'msw'
import { server } from '../test/mocks/server'
import { mockFeedGroups } from '../test/mocks/handlers'
import { HomePage } from './HomePage'
import type { WatchlistPackageDto } from '../api/generated/model'

const mockReactWatchlistItem: WatchlistPackageDto = {
  id: 'pkg-react-test-id',
  name: 'react',
  githubOwner: 'facebook',
  githubRepo: 'react',
  npmName: 'react',
}

// Mock @tanstack/react-router Link to avoid RouterProvider requirement
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: Record<string, unknown>) => (
    <a href={to as string} {...props}>
      {children as React.ReactNode}
    </a>
  ),
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: '/' }),
}))

// Override the global @stytch/react mock so we can control auth state per-test
const mockStytchUser = vi.hoisted(() => vi.fn())

vi.mock('@stytch/react', () => ({
  StytchProvider: ({ children }: { children: React.ReactNode }) => children,
  useStytch: () => ({
    magicLinks: { authenticate: vi.fn() },
    oauth: { authenticate: vi.fn() },
    session: { revoke: vi.fn() },
  }),
  useStytchUser: mockStytchUser,
}))

const authenticatedUser = {
  user: {
    user_id: 'test-user-id',
    emails: [{ email: 'test@example.com' }],
    roles: ['patch_notes_admin'],
  },
  isInitialized: true,
}

const anonymousUser = {
  user: null,
  isInitialized: true,
}

beforeEach(() => {
  localStorage.clear()
})

describe('HomePage', () => {
  describe('anonymous user', () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(anonymousUser)
    })

    it('sees releases with default backend filtering', async () => {
      render(<HomePage />)

      await waitFor(() => {
        expect(screen.getByText('react')).toBeInTheDocument()
      })
      expect(screen.getByText('lodash')).toBeInTheDocument()
    })

    it('renders AI summary as markdown with headings and lists', async () => {
      render(<HomePage />)

      await waitFor(() => {
        expect(screen.getByText('react')).toBeInTheDocument()
      })
      // The markdown headings should be rendered (h2 → h4 in card)
      expect(screen.getByText('TL;DR')).toBeInTheDocument()
      expect(screen.getByText('Breaking')).toBeInTheDocument()
      // Bullet list items should be rendered
      expect(screen.getByText('Removed legacy context API')).toBeInTheDocument()
    })

    it('sees the hero card', async () => {
      render(<HomePage />)

      await waitFor(() => {
        expect(
          screen.getByText(/Never miss a release that matters/)
        ).toBeInTheDocument()
      })
    })

    it('does not see the empty watchlist prompt', async () => {
      render(<HomePage />)

      await waitFor(() => {
        expect(
          screen.getByText(/Never miss a release that matters/)
        ).toBeInTheDocument()
      })
      expect(
        screen.queryByText(/Add packages to your watchlist/)
      ).not.toBeInTheDocument()
    })
  })

  describe('authenticated user with watchlist', () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(authenticatedUser)
    })

    it('sees releases filtered by watchlist', async () => {
      // Watchlist contains only React — feed endpoint handles filtering server-side
      server.use(
        http.get('/api/watchlist', () => {
          return HttpResponse.json([mockReactWatchlistItem])
        }),
        http.get('/api/feed', () => {
          // Server returns only React groups for this user's watchlist
          return HttpResponse.json({
            groups: mockFeedGroups.filter(
              (g) => g.packageId === 'pkg-react-test-id'
            ),
          })
        })
      )

      render(<HomePage />)

      await waitFor(() => {
        expect(screen.getAllByText('react').length).toBeGreaterThan(0)
      })
      // Lodash releases should not appear since it's not in the watchlist
      expect(screen.queryByText('v4.x')).not.toBeInTheDocument()
    })

    it('does not show hero card', async () => {
      server.use(
        http.get('/api/watchlist', () => {
          return HttpResponse.json([mockReactWatchlistItem])
        })
      )

      render(<HomePage />)

      await waitFor(() => {
        expect(screen.getAllByText('react').length).toBeGreaterThan(0)
      })
      expect(
        screen.queryByText(/Never miss a release that matters/)
      ).not.toBeInTheDocument()
    })
  })

  describe('prerelease type detection', () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(anonymousUser)
    })

    it('detects .NET-style preview tags and renders preview badge', async () => {
      const previewGroup: (typeof mockFeedGroups)[number] = {
        packageId: 'pkg-dotnet-test-id',
        packageName: 'dotnet-runtime',
        npmName: null,
        githubOwner: 'dotnet',
        githubRepo: 'runtime',
        majorVersion: 11,
        isPrerelease: true,
        versionRange: 'v11.x',
        summary: null,
        releaseCount: 1,
        lastUpdated: '2026-02-01T00:00:00Z',
        releases: [
          {
            id: 'rel-dotnet-preview-test-id',
            tag: 'v11.0.0-preview.1.26104.118',
            title: '.NET 11 Preview 1',
            publishedAt: '2026-02-01T00:00:00Z',
          },
        ],
      }

      server.use(
        http.get('/api/feed', () => {
          return HttpResponse.json({ groups: [previewGroup] })
        })
      )

      render(<HomePage />)

      await waitFor(() => {
        expect(screen.getByText('dotnet/runtime')).toBeInTheDocument()
      })
      expect(screen.getByText('preview')).toBeInTheDocument()
    })
  })

  describe('authenticated user with empty watchlist', () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(authenticatedUser)
      server.use(
        http.get('/api/watchlist', () => {
          return HttpResponse.json([])
        })
      )
    })

    it('sees prompt to add packages', async () => {
      render(<HomePage />)

      await waitFor(() => {
        expect(
          screen.getByText(/Add packages to your watchlist/)
        ).toBeInTheDocument()
      })
    })

    it('does not show hero card', async () => {
      render(<HomePage />)

      await waitFor(() => {
        expect(
          screen.getByText(/Add packages to your watchlist/)
        ).toBeInTheDocument()
      })
      expect(
        screen.queryByText(/Never miss a release that matters/)
      ).not.toBeInTheDocument()
    })
  })
})
