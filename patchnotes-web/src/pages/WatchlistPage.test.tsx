import { render, screen, waitFor } from "../test/utils";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "../test/mocks/server";
import { WatchlistPage } from "./WatchlistPage";
import type { WatchlistPackageDto } from "../api/generated/model";

const mockReactPackage: WatchlistPackageDto = {
  id: "pkg-react-test-id",
  name: "react",
  githubOwner: "facebook",
  githubRepo: "react",
  npmName: "react",
};

const mockLodashPackage: WatchlistPackageDto = {
  id: "pkg-lodash-test-id",
  name: "lodash",
  githubOwner: "lodash",
  githubRepo: "lodash",
  npmName: "lodash",
};

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children, to, ...props }: Record<string, unknown>) => (
    <a href={to as string} {...props}>
      {children as React.ReactNode}
    </a>
  ),
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: "/watchlist" }),
}));

const mockStytchUser = vi.hoisted(() => vi.fn());

vi.mock("@stytch/react", () => ({
  StytchProvider: ({ children }: { children: React.ReactNode }) => children,
  useStytch: () => ({
    magicLinks: { authenticate: vi.fn() },
    oauth: { authenticate: vi.fn() },
    session: { revoke: vi.fn() },
  }),
  useStytchUser: mockStytchUser,
}));

const authenticatedUser = {
  user: {
    user_id: "test-user-id",
    emails: [{ email: "test@example.com" }],
  },
  isInitialized: true,
};

const anonymousUser = {
  user: null,
  isInitialized: true,
};

describe("WatchlistPage", () => {
  describe("unauthenticated", () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(anonymousUser);
    });

    it("shows loading state when not initialized", () => {
      mockStytchUser.mockReturnValue({ user: null, isInitialized: false });
      render(<WatchlistPage />);
      expect(screen.getByText("Loading...")).toBeInTheDocument();
    });
  });

  describe("authenticated with empty watchlist", () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(authenticatedUser);
      server.use(
        http.get("/api/watchlist", () => {
          return HttpResponse.json([]);
        }),
      );
    });

    it("shows empty watchlist message", async () => {
      render(<WatchlistPage />);

      await waitFor(() => {
        expect(screen.getByText(/Your watchlist is empty/)).toBeInTheDocument();
      });
    });

    it("shows search input", async () => {
      render(<WatchlistPage />);

      await waitFor(() => {
        expect(screen.getByPlaceholderText(/Search GitHub repositories/)).toBeInTheDocument();
      });
    });

    it("shows hint to type at least 2 characters", async () => {
      render(<WatchlistPage />);

      await waitFor(() => {
        expect(screen.getByText(/Type at least 2 characters/)).toBeInTheDocument();
      });
    });
  });

  describe("authenticated with watchlist items", () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(authenticatedUser);
      server.use(
        http.get("/api/watchlist", () => {
          return HttpResponse.json([mockReactPackage]);
        }),
      );
    });

    it("displays watched packages", async () => {
      render(<WatchlistPage />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });
      expect(screen.getByText("1 package")).toBeInTheDocument();
    });

    it("shows package count", async () => {
      server.use(
        http.get("/api/watchlist", () => {
          return HttpResponse.json([mockReactPackage, mockLodashPackage]);
        }),
      );

      render(<WatchlistPage />);

      await waitFor(() => {
        expect(screen.getByText("2 packages")).toBeInTheDocument();
      });
    });
  });

  describe("GitHub search", () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(authenticatedUser);
      server.use(
        http.get("/api/watchlist", () => {
          return HttpResponse.json([]);
        }),
      );
    });

    it("shows search results after typing", async () => {
      const user = userEvent.setup();
      render(<WatchlistPage />);

      const input = screen.getByPlaceholderText(/Search GitHub repositories/);
      await user.type(input, "react");

      await waitFor(() => {
        expect(screen.getByText("facebook/react")).toBeInTheDocument();
      });
      expect(screen.getByText("A library for building UIs")).toBeInTheDocument();
    });

    it("shows no results message when search returns empty", async () => {
      server.use(
        http.get("/api/github/search", () => {
          return HttpResponse.json([]);
        }),
      );

      const user = userEvent.setup();
      render(<WatchlistPage />);

      const input = screen.getByPlaceholderText(/Search GitHub repositories/);
      await user.type(input, "nonexistentpackage");

      await waitFor(() => {
        expect(screen.getByText(/No repositories found/)).toBeInTheDocument();
      });
    });

    it("marks already-watched packages in search results", async () => {
      server.use(
        http.get("/api/watchlist", () => {
          return HttpResponse.json([mockReactPackage]);
        }),
      );

      const user = userEvent.setup();
      render(<WatchlistPage />);

      const input = screen.getByPlaceholderText(/Search GitHub repositories/);
      await user.type(input, "react");

      await waitFor(() => {
        expect(screen.getByText("Already added")).toBeInTheDocument();
      });
    });
  });
});
