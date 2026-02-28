import { describe, it, expect, vi, beforeEach } from "vitest";

const { mockSend, mockFindUnique, mockFindFirst, mockFindMany, mockRenderTemplate, mockInterpolateSubject, mockTrackEvent, mockFlush } = vi.hoisted(() => ({
    mockSend: vi.fn(),
    mockFindUnique: vi.fn(),
    mockFindFirst: vi.fn(),
    mockFindMany: vi.fn(),
    mockRenderTemplate: vi.fn(),
    mockInterpolateSubject: vi.fn(),
    mockTrackEvent: vi.fn(),
    mockFlush: vi.fn().mockResolvedValue(undefined),
}));

vi.mock("../lib/resend", () => ({
    resend: { emails: { send: mockSend } },
    FROM_ADDRESS: "PatchNotes <notifications@patchnotes.dev>",
    sanitizeSubject: (s: string) => s.replace(/[\r\n]+/g, " ").trim(),
    isValidEmail: (email: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email),
}));

vi.mock("../lib/prisma", () => ({
    getPrismaClient: () => ({
        emailTemplates: { findUnique: mockFindUnique },
        users: { findFirst: mockFindFirst },
        releases: { findMany: mockFindMany },
    }),
}));

vi.mock("../lib/templateRenderer", () => ({
    renderTemplate: mockRenderTemplate,
    interpolateSubject: mockInterpolateSubject,
}));

vi.mock("../lib/telemetry", () => ({
    trackEvent: mockTrackEvent,
    trackException: vi.fn(),
    flush: mockFlush,
}));

import { sendTestEmail } from "./sendTestEmail";

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

const VALID_REQUEST_WITH_DATA = {
    templateName: "welcome",
    recipientEmail: "admin@test.com",
    testData: { name: "Jane Doe" },
};

const VALID_REQUEST_REAL_DATA = {
    templateName: "welcome",
    recipientEmail: "admin@test.com",
};

describe("sendTestEmail", () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockFindUnique.mockResolvedValue(null);
        mockFindFirst.mockResolvedValue(null);
        mockFindMany.mockResolvedValue([]);
    });

    // ── Validation ────────────────────────────────────────────

    it("returns 400 for invalid JSON", async () => {
        const request = { json: async () => { throw new Error("bad json"); } } as any;
        const result = await sendTestEmail(request, makeContext());
        expect(result.status).toBe(400);
        expect(result.body).toBe("Invalid JSON body");
    });

    it("returns 400 when templateName is missing", async () => {
        const result = await sendTestEmail(
            makeRequest({ ...VALID_REQUEST_WITH_DATA, templateName: "" }),
            makeContext()
        );
        expect(result.status).toBe(400);
        expect(result.body).toBe("Missing required field: templateName");
    });

    it("returns 400 when recipientEmail is missing", async () => {
        const result = await sendTestEmail(
            makeRequest({ ...VALID_REQUEST_WITH_DATA, recipientEmail: "" }),
            makeContext()
        );
        expect(result.status).toBe(400);
        expect(result.body).toBe("Missing required field: recipientEmail");
    });

    it("returns 400 for invalid email format", async () => {
        const result = await sendTestEmail(
            makeRequest({ ...VALID_REQUEST_WITH_DATA, recipientEmail: "not-valid" }),
            makeContext()
        );
        expect(result.status).toBe(400);
        expect(result.body).toBe("Invalid email address format");
    });

    it("returns 404 when template is not found", async () => {
        const result = await sendTestEmail(makeRequest(VALID_REQUEST_REAL_DATA), makeContext());
        expect(result.status).toBe(404);
        expect(result.body).toBe("Template not found: welcome");
    });

    // ── With sample data (testData provided) ──────────────────

    it("sends test email with provided testData", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "welcome",
            Subject: "Welcome, {{name}}!",
            JsxSource: "<jsx/>",
        });
        mockRenderTemplate.mockResolvedValue("<html>welcome rendered</html>");
        mockInterpolateSubject.mockReturnValue("Welcome, Jane Doe!");
        mockSend.mockResolvedValue({ error: null });

        const result = await sendTestEmail(makeRequest(VALID_REQUEST_WITH_DATA), makeContext());

        expect(result.status).toBe(200);
        expect(mockRenderTemplate).toHaveBeenCalledWith("<jsx/>", { name: "Jane Doe" });
        expect(mockSend.mock.calls[0][0].subject).toBe("[TEST] Welcome, Jane Doe!");
        expect(mockTrackEvent).toHaveBeenCalledWith("TestEmailSent", expect.objectContaining({
            dataSource: "sample",
        }));
    });

    it("derives count from releases array in provided testData", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "digest",
            Subject: "Your Digest — {{count}} updates",
            JsxSource: "<digest-jsx/>",
        });
        mockRenderTemplate.mockResolvedValue("<html>digest</html>");
        mockInterpolateSubject.mockReturnValue("Your Digest — 2 updates");
        mockSend.mockResolvedValue({ error: null });

        const result = await sendTestEmail(makeRequest({
            templateName: "digest",
            recipientEmail: "admin@test.com",
            testData: {
                name: "Jane Doe",
                releases: [
                    { packageName: "react", version: "19.1.0", summary: "New features" },
                    { packageName: "lodash", version: "5.0.0", summary: "ES modules" },
                ],
            },
        }), makeContext());

        expect(result.status).toBe(200);
        expect(mockInterpolateSubject).toHaveBeenCalledWith(
            "Your Digest — {{count}} updates",
            expect.objectContaining({ count: "2" })
        );
    });

    // ── With real data (testData omitted) ─────────────────────

    it("uses real DB data for welcome when testData is omitted", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "welcome",
            Subject: "Welcome, {{name}}!",
            JsxSource: "<jsx/>",
        });
        mockFindFirst.mockResolvedValue({ Name: "Alice Admin" });
        mockRenderTemplate.mockResolvedValue("<html>welcome Alice</html>");
        mockInterpolateSubject.mockReturnValue("Welcome, Alice Admin!");
        mockSend.mockResolvedValue({ error: null });

        const result = await sendTestEmail(makeRequest(VALID_REQUEST_REAL_DATA), makeContext());

        expect(result.status).toBe(200);
        expect(mockRenderTemplate).toHaveBeenCalledWith("<jsx/>", { name: "Alice Admin" });
        expect(mockTrackEvent).toHaveBeenCalledWith("TestEmailSent", expect.objectContaining({
            dataSource: "database",
        }));
    });

    it("uses 'there' as fallback name when user not found in DB", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "welcome",
            Subject: "Welcome, {{name}}!",
            JsxSource: "<jsx/>",
        });
        mockFindFirst.mockResolvedValue(null);
        mockRenderTemplate.mockResolvedValue("<html>welcome</html>");
        mockInterpolateSubject.mockReturnValue("Welcome, there!");
        mockSend.mockResolvedValue({ error: null });

        const result = await sendTestEmail(makeRequest(VALID_REQUEST_REAL_DATA), makeContext());

        expect(result.status).toBe(200);
        expect(mockRenderTemplate).toHaveBeenCalledWith("<jsx/>", { name: "there" });
    });

    it("uses real DB releases for digest when testData is omitted", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "digest",
            Subject: "Digest — {{count}} updates",
            JsxSource: "<digest-jsx/>",
        });
        mockFindFirst.mockResolvedValue({ Name: "Alice" });
        mockFindMany.mockResolvedValue([
            {
                Tag: "v19.1.0",
                MajorVersion: 19,
                IsPrerelease: false,
                Packages: {
                    Name: "react",
                    ReleaseSummaries: [
                        { Summary: "New features", MajorVersion: 19, IsPrerelease: false },
                    ],
                },
            },
        ]);
        mockRenderTemplate.mockResolvedValue("<html>digest</html>");
        mockInterpolateSubject.mockReturnValue("Digest — 1 updates");
        mockSend.mockResolvedValue({ error: null });

        const result = await sendTestEmail(makeRequest({
            templateName: "digest",
            recipientEmail: "admin@test.com",
        }), makeContext());

        expect(result.status).toBe(200);
        expect(mockRenderTemplate).toHaveBeenCalledWith("<digest-jsx/>", {
            name: "Alice",
            releases: [{ packageName: "react", version: "v19.1.0", summary: "New features" }],
        });
        expect(mockInterpolateSubject).toHaveBeenCalledWith(
            "Digest — {{count}} updates",
            expect.objectContaining({ count: "1" })
        );
    });

    // ── Error paths ───────────────────────────────────────────

    it("returns 500 when template rendering fails (no fallback)", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "welcome",
            Subject: "Welcome, {{name}}!",
            JsxSource: "bad-jsx",
        });
        mockRenderTemplate.mockRejectedValue(new Error("render failed"));

        const result = await sendTestEmail(makeRequest(VALID_REQUEST_WITH_DATA), makeContext());

        expect(result.status).toBe(500);
        expect(result.body).toBe("Internal server error");
        expect(mockSend).not.toHaveBeenCalled();
    });

    it("returns 500 when resend returns an error", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "welcome",
            Subject: "Welcome, {{name}}!",
            JsxSource: "<jsx/>",
        });
        mockRenderTemplate.mockResolvedValue("<html>rendered</html>");
        mockInterpolateSubject.mockReturnValue("Welcome, Jane Doe!");
        mockSend.mockResolvedValue({ error: { message: "rate limited" } });

        const result = await sendTestEmail(makeRequest(VALID_REQUEST_WITH_DATA), makeContext());

        expect(result.status).toBe(500);
        expect(result.body).toContain("Failed to send email");
        expect(mockTrackEvent).toHaveBeenCalledWith("TestEmailFailed", expect.objectContaining({
            reason: "resend_error",
        }));
    });
});
