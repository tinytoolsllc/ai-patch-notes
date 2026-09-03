import { render, screen, waitFor } from "../test/utils";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "../test/mocks/server";
import { mockPackages } from "../test/mocks/handlers";
import { AdminReset } from "./AdminReset";

// Mock @tanstack/react-router
const mockNavigate = vi.fn();
vi.mock("@tanstack/react-router", () => ({
  Link: ({ children, to, ...props }: Record<string, unknown>) => (
    <a href={to as string} {...props}>
      {children as React.ReactNode}
    </a>
  ),
  useNavigate: () => mockNavigate,
  useLocation: () => ({ pathname: "/admin" }),
}));

// Override the global @stytch/react mock to control auth state per-test
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

const adminUser = {
  user: {
    user_id: "test-user-id",
    emails: [{ email: "admin@example.com" }],
    roles: ["patch_notes_admin"],
  },
  isInitialized: true,
};

const nonAdminUser = {
  user: {
    user_id: "test-user-id",
    emails: [{ email: "user@example.com" }],
    roles: [],
  },
  isInitialized: true,
};

beforeEach(() => {
  mockNavigate.mockClear();
});

describe("AdminReset", () => {
  describe("when user is admin", () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(adminUser);
    });

    it("renders the packages table", async () => {
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      expect(screen.getByText("lodash")).toBeInTheDocument();
    });

    it("shows Reset Summaries buttons for each package", async () => {
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      const resetButtons = screen.getAllByText("Reset Summaries");
      expect(resetButtons).toHaveLength(mockPackages.length);
    });

    it("shows Reset Releases buttons for each package", async () => {
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      const resetButtons = screen.getAllByText("Reset Releases");
      expect(resetButtons).toHaveLength(mockPackages.length);
    });

    it("shows Delete buttons for each package", async () => {
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      const deleteButtons = screen.getAllByText("Delete");
      expect(deleteButtons).toHaveLength(mockPackages.length);
    });

    it("shows confirmation modal when Reset Summaries is clicked", async () => {
      const user = userEvent.setup();
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      const resetButtons = screen.getAllByText("Reset Summaries");
      await user.click(resetButtons[0]);

      expect(screen.getByText(/mark all releases as needing new summaries/i)).toBeInTheDocument();
    });

    it("shows confirmation modal when Reset Releases is clicked", async () => {
      const user = userEvent.setup();
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      const resetButtons = screen.getAllByText("Reset Releases");
      await user.click(resetButtons[0]);

      expect(screen.getByText(/delete all releases and summaries/i)).toBeInTheDocument();
    });

    it("shows confirmation modal when Delete is clicked", async () => {
      const user = userEvent.setup();
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      const deleteButtons = screen.getAllByText("Delete");
      await user.click(deleteButtons[0]);

      expect(screen.getByText(/permanently delete this package/i)).toBeInTheDocument();
    });

    it("closes modal when Cancel is clicked", async () => {
      const user = userEvent.setup();
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      const resetButtons = screen.getAllByText("Reset Summaries");
      await user.click(resetButtons[0]);

      expect(screen.getByText(/mark all releases as needing new summaries/i)).toBeInTheDocument();

      await user.click(screen.getByText("Cancel"));

      await waitFor(() => {
        expect(
          screen.queryByText(/mark all releases as needing new summaries/i),
        ).not.toBeInTheDocument();
      });
    });

    it("calls reset summaries API on confirm", async () => {
      let resetCalled = false;
      server.use(
        http.post("/api/admin/packages/:id/reset-summaries", () => {
          resetCalled = true;
          return new HttpResponse(null, { status: 204 });
        }),
      );

      const user = userEvent.setup();
      render(<AdminReset />);

      await waitFor(() => {
        expect(screen.getByText("react")).toBeInTheDocument();
      });

      const resetButtons = screen.getAllByText("Reset Summaries");
      await user.click(resetButtons[0]);

      // The modal has its own "Reset Summaries" confirm button alongside the row buttons.
      // Find all buttons with that text and click the last one (the modal confirm).
      const confirmButtons = screen.getAllByRole("button", {
        name: "Reset Summaries",
      });
      await user.click(confirmButtons[confirmButtons.length - 1]);

      await waitFor(() => {
        expect(resetCalled).toBe(true);
      });
    });

    it("shows loading state", () => {
      server.use(
        http.get("/api/packages", () => {
          return new Promise(() => {}); // never resolves
        }),
      );

      render(<AdminReset />);

      expect(screen.getByText("Loading packages...")).toBeInTheDocument();
    });
  });

  describe("when user is not admin", () => {
    beforeEach(() => {
      mockStytchUser.mockReturnValue(nonAdminUser);
    });

    it("redirects non-admin users", async () => {
      render(<AdminReset />);

      await waitFor(() => {
        expect(mockNavigate).toHaveBeenCalledWith({ to: "/" });
      });
    });
  });
});
