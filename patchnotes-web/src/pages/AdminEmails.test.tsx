import { render, screen, waitFor } from "../test/utils";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "../test/mocks/server";
import { AdminEmails } from "./AdminEmails";

const API_BASE = import.meta.env.VITE_API_URL + "/api";

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children, to, ...props }: Record<string, unknown>) => (
    <a href={to as string} {...props}>
      {children as React.ReactNode}
    </a>
  ),
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: "/admin/emails" }),
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

const adminUser = {
  user: {
    user_id: "test-admin-id",
    emails: [{ email: "admin@test.com" }],
    roles: ["patch_notes_admin"],
  },
  isInitialized: true,
};

const mockTemplates = [
  {
    id: "tmpl-1",
    name: "welcome",
    subject: "Welcome, {{name}}!",
    jsxSource: "<div>Welcome</div>",
    updatedAt: "2025-01-01T00:00:00Z",
  },
  {
    id: "tmpl-2",
    name: "digest",
    subject: "Digest — {{count}} updates",
    jsxSource: "<div>Digest</div>",
    updatedAt: "2025-01-01T00:00:00Z",
  },
];

describe("AdminEmails - Send Test Email", () => {
  beforeEach(() => {
    mockStytchUser.mockReturnValue(adminUser);
    server.use(
      http.get(`${API_BASE}/admin/email-templates`, () => {
        return HttpResponse.json(mockTemplates);
      }),
    );
  });

  it("shows Send Test Email button when not editing", async () => {
    render(<AdminEmails />);

    await waitFor(() => {
      expect(screen.getByText("Send Test Email")).toBeInTheDocument();
    });
  });

  it("opens test email card with prefilled email on click", async () => {
    const user = userEvent.setup();
    render(<AdminEmails />);

    await waitFor(() => {
      expect(screen.getByText("Send Test Email")).toBeInTheDocument();
    });

    await user.click(screen.getByText("Send Test Email"));

    expect(screen.getByText("Recipient Email")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("admin@example.com")).toHaveValue("admin@test.com");
  });

  it("closes test email card on Cancel", async () => {
    const user = userEvent.setup();
    render(<AdminEmails />);

    await waitFor(() => {
      expect(screen.getByText("Send Test Email")).toBeInTheDocument();
    });

    await user.click(screen.getByText("Send Test Email"));
    expect(screen.getByText("Recipient Email")).toBeInTheDocument();

    await user.click(screen.getByText("Cancel"));
    expect(screen.queryByText("Recipient Email")).not.toBeInTheDocument();
  });

  it("sends test email and shows success", async () => {
    const user = userEvent.setup();
    render(<AdminEmails />);

    await waitFor(() => {
      expect(screen.getByText("Send Test Email")).toBeInTheDocument();
    });

    await user.click(screen.getByText("Send Test Email"));
    await user.click(screen.getByText("Send"));

    await waitFor(() => {
      expect(screen.getByText("Test email sent to admin@test.com")).toBeInTheDocument();
    });
  });

  it("shows error on send failure", async () => {
    server.use(
      http.post(`${API_BASE}/admin/email-templates/:id/test`, () => {
        return HttpResponse.json({ error: "Email function error" }, { status: 500 });
      }),
    );

    const user = userEvent.setup();
    render(<AdminEmails />);

    await waitFor(() => {
      expect(screen.getByText("Send Test Email")).toBeInTheDocument();
    });

    await user.click(screen.getByText("Send Test Email"));
    await user.click(screen.getByText("Send"));

    await waitFor(() => {
      expect(screen.getByText("Failed to send test email")).toBeInTheDocument();
    });
  });

  it("disables Send button when email is empty", async () => {
    const user = userEvent.setup();
    render(<AdminEmails />);

    await waitFor(() => {
      expect(screen.getByText("Send Test Email")).toBeInTheDocument();
    });

    await user.click(screen.getByText("Send Test Email"));

    const emailInput = screen.getByPlaceholderText("admin@example.com");
    await user.clear(emailInput);

    expect(screen.getByText("Send")).toBeDisabled();
  });
});
