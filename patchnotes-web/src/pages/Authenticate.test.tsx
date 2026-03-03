import { render, screen } from '../test/utils'
import { Authenticate } from './Authenticate'

const mockNavigate = vi.fn()

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: Record<string, unknown>) => (
    <a href={to as string} {...props}>
      {children as React.ReactNode}
    </a>
  ),
  useNavigate: () => mockNavigate,
  useLocation: () => ({ pathname: '/authenticate' }),
}))

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

function setLocationSearch(search: string) {
  Object.defineProperty(window, 'location', {
    value: { search },
    writable: true,
    configurable: true,
  })
}

describe('Authenticate', () => {
  beforeEach(() => {
    mockNavigate.mockClear()
  })

  describe('return URL handling', () => {
    it('redirects to / when no returnUrl in query params', () => {
      setLocationSearch('?token=t&stytch_token_type=magic_links')
      mockStytchUser.mockReturnValue({
        user: { user_id: 'test', emails: [] },
        isInitialized: true,
      })

      render(<Authenticate />)

      expect(mockNavigate).toHaveBeenCalledWith({ to: '/' })
    })

    it('redirects to returnUrl from query params after auth', () => {
      setLocationSearch(
        '?token=t&stytch_token_type=magic_links&returnUrl=%2Fpackages%2Ffacebook%2Freact'
      )
      mockStytchUser.mockReturnValue({
        user: { user_id: 'test', emails: [] },
        isInitialized: true,
      })

      render(<Authenticate />)

      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/packages/facebook/react',
      })
    })

    it('rejects absolute URLs (open redirect prevention)', () => {
      setLocationSearch(
        '?token=t&stytch_token_type=magic_links&returnUrl=https%3A%2F%2Fevil.com'
      )
      mockStytchUser.mockReturnValue({
        user: { user_id: 'test', emails: [] },
        isInitialized: true,
      })

      render(<Authenticate />)

      expect(mockNavigate).toHaveBeenCalledWith({ to: '/' })
    })
  })

  it('shows error when no token provided', () => {
    setLocationSearch('')
    mockStytchUser.mockReturnValue({ user: null, isInitialized: true })

    render(<Authenticate />)

    expect(screen.getByText('Invalid authentication link')).toBeInTheDocument()
  })
})
