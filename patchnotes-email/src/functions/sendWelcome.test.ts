import { describe, it, expect, vi, beforeEach } from "vitest";

const { mockSend, mockFindUnique, mockRenderTemplate, mockInterpolateSubject } = vi.hoisted(() => ({
  mockSend: vi.fn(),
  mockFindUnique: vi.fn(),
  mockRenderTemplate: vi.fn(),
  mockInterpolateSubject: vi.fn(),
}));

vi.mock("../lib/resend", () => ({
  resend: { emails: { send: mockSend } },
  FROM_ADDRESS: "PatchNotes <notifications@patchnotes.dev>",
  escapeHtml: (s: string) =>
    s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"),
  emailFooter: () => "<footer/>",
  sanitizeSubject: (s: string) => s.replace(/[\r\n]+/g, " ").trim(),
  isValidEmail: (email: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email),
}));

vi.mock("../lib/prisma", () => ({
  getPrismaClient: () => ({
    emailTemplates: { findUnique: mockFindUnique },
  }),
}));

vi.mock("../lib/templateRenderer", () => ({
  renderTemplate: mockRenderTemplate,
  interpolateSubject: mockInterpolateSubject,
}));

import { sendWelcome } from "./sendWelcome";

function makeContext() {
  return {
    log: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
  } as any;
}

function makeRequest(body: unknown): any {
  return {
    json: async () => body,
  };
}

describe("sendWelcome", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFindUnique.mockResolvedValue(null);
  });

  it("returns 400 for invalid JSON", async () => {
    const request = {
      json: async () => {
        throw new Error("bad json");
      },
    } as any;
    const result = await sendWelcome(request, makeContext());
    expect(result.status).toBe(400);
    expect(result.body).toBe("Invalid JSON body");
  });

  it("returns 400 when email or name is missing", async () => {
    const result = await sendWelcome(makeRequest({ email: "a@b.com" }), makeContext());
    expect(result.status).toBe(400);
    expect(result.body).toBe("Missing required fields: email, name");
  });

  it("returns 400 for invalid email format", async () => {
    const result = await sendWelcome(
      makeRequest({ email: "not-valid", name: "Test" }),
      makeContext(),
    );
    expect(result.status).toBe(400);
    expect(result.body).toBe("Invalid email address format");
  });

  it("sends welcome email with fallback HTML when no template in DB", async () => {
    mockSend.mockResolvedValue({ error: null });
    const context = makeContext();

    const result = await sendWelcome(makeRequest({ email: "a@test.com", name: "Alice" }), context);

    expect(result.status).toBe(200);
    expect(mockSend).toHaveBeenCalledTimes(1);
    const call = mockSend.mock.calls[0][0];
    expect(call.html).toContain("Welcome to PatchNotes, Alice!");
    expect(call.subject).toBe("Welcome to PatchNotes, Alice!");
    expect(call.to).toBe("a@test.com");
  });

  it("uses DB template when available", async () => {
    mockFindUnique.mockResolvedValue({
      Name: "welcome",
      Subject: "Welcome, {{name}}!",
      JsxSource: "<fake-jsx/>",
    });
    mockRenderTemplate.mockResolvedValue("<html>welcome rendered</html>");
    mockInterpolateSubject.mockReturnValue("Welcome, Alice!");
    mockSend.mockResolvedValue({ error: null });
    const context = makeContext();

    const result = await sendWelcome(makeRequest({ email: "a@test.com", name: "Alice" }), context);

    expect(result.status).toBe(200);
    expect(mockRenderTemplate).toHaveBeenCalledWith("<fake-jsx/>", { name: "Alice" });
    expect(mockInterpolateSubject).toHaveBeenCalledWith("Welcome, {{name}}!", { name: "Alice" });
    expect(mockSend).toHaveBeenCalledWith(
      expect.objectContaining({
        html: "<html>welcome rendered</html>",
        subject: "Welcome, Alice!",
      }),
    );
  });

  it("falls back to hardcoded HTML when template rendering fails", async () => {
    mockFindUnique.mockResolvedValue({
      Name: "welcome",
      Subject: "Welcome",
      JsxSource: "bad-jsx",
    });
    mockRenderTemplate.mockRejectedValue(new Error("render failed"));
    mockSend.mockResolvedValue({ error: null });
    const context = makeContext();

    const result = await sendWelcome(makeRequest({ email: "a@test.com", name: "Alice" }), context);

    expect(result.status).toBe(200);
    expect(context.warn).toHaveBeenCalledWith(
      expect.stringContaining("Failed to render welcome template"),
      expect.any(Error),
    );
    const sentHtml = mockSend.mock.calls[0][0].html;
    expect(sentHtml).toContain("Welcome to PatchNotes, Alice!");
  });

  it("returns 500 when resend returns an error", async () => {
    mockSend.mockResolvedValue({ error: { message: "rate limited" } });
    const context = makeContext();

    const result = await sendWelcome(makeRequest({ email: "a@test.com", name: "Alice" }), context);

    expect(result.status).toBe(500);
    expect(result.body).toContain("Failed to send email");
  });

  it("returns 500 on unexpected error", async () => {
    mockSend.mockRejectedValue(new Error("network failure"));
    const context = makeContext();

    const result = await sendWelcome(makeRequest({ email: "a@test.com", name: "Alice" }), context);

    expect(result.status).toBe(500);
    expect(result.body).toBe("Internal server error");
  });
});
