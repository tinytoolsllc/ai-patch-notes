import { describe, it, expect, vi, beforeEach } from "vitest";

const { mockSend, mockFindUnique, mockRenderTemplate, mockInterpolateSubject, mockTrackEvent, mockFlush } = vi.hoisted(() => ({
    mockSend: vi.fn(),
    mockFindUnique: vi.fn(),
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

const VALID_REQUEST = {
    templateName: "welcome",
    recipientEmail: "admin@test.com",
    testData: { name: "Jane Doe" },
};

describe("sendTestEmail", () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockFindUnique.mockResolvedValue(null);
    });

    it("returns 400 for invalid JSON", async () => {
        const request = { json: async () => { throw new Error("bad json"); } } as any;
        const result = await sendTestEmail(request, makeContext());
        expect(result.status).toBe(400);
        expect(result.body).toBe("Invalid JSON body");
    });

    it("returns 400 when templateName is missing", async () => {
        const result = await sendTestEmail(
            makeRequest({ ...VALID_REQUEST, templateName: "" }),
            makeContext()
        );
        expect(result.status).toBe(400);
        expect(result.body).toBe("Missing required field: templateName");
    });

    it("returns 400 when recipientEmail is missing", async () => {
        const result = await sendTestEmail(
            makeRequest({ ...VALID_REQUEST, recipientEmail: "" }),
            makeContext()
        );
        expect(result.status).toBe(400);
        expect(result.body).toBe("Missing required field: recipientEmail");
    });

    it("returns 400 for invalid email format", async () => {
        const result = await sendTestEmail(
            makeRequest({ ...VALID_REQUEST, recipientEmail: "not-valid" }),
            makeContext()
        );
        expect(result.status).toBe(400);
        expect(result.body).toBe("Invalid email address format");
    });

    it("returns 400 when testData is missing", async () => {
        const result = await sendTestEmail(
            makeRequest({ templateName: "welcome", recipientEmail: "a@b.com" }),
            makeContext()
        );
        expect(result.status).toBe(400);
        expect(result.body).toBe("Missing required field: testData");
    });

    it("returns 404 when template is not found", async () => {
        const result = await sendTestEmail(makeRequest(VALID_REQUEST), makeContext());
        expect(result.status).toBe(404);
        expect(result.body).toBe("Template not found: welcome");
    });

    it("returns 500 when template rendering fails (no fallback)", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "welcome",
            Subject: "Welcome, {{name}}!",
            JsxSource: "bad-jsx",
        });
        mockRenderTemplate.mockRejectedValue(new Error("render failed"));

        const result = await sendTestEmail(makeRequest(VALID_REQUEST), makeContext());

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

        const result = await sendTestEmail(makeRequest(VALID_REQUEST), makeContext());

        expect(result.status).toBe(500);
        expect(result.body).toContain("Failed to send email");
        expect(mockTrackEvent).toHaveBeenCalledWith("TestEmailFailed", expect.objectContaining({
            reason: "resend_error",
        }));
    });

    it("sends test email successfully with [TEST] subject prefix", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "welcome",
            Subject: "Welcome, {{name}}!",
            JsxSource: "<jsx/>",
        });
        mockRenderTemplate.mockResolvedValue("<html>welcome rendered</html>");
        mockInterpolateSubject.mockReturnValue("Welcome, Jane Doe!");
        mockSend.mockResolvedValue({ error: null });

        const result = await sendTestEmail(makeRequest(VALID_REQUEST), makeContext());

        expect(result.status).toBe(200);
        expect(result.body).toBe("Test email sent");

        expect(mockRenderTemplate).toHaveBeenCalledWith("<jsx/>", { name: "Jane Doe" });
        expect(mockInterpolateSubject).toHaveBeenCalledWith(
            "Welcome, {{name}}!",
            expect.objectContaining({ name: "Jane Doe" })
        );

        const sendCall = mockSend.mock.calls[0][0];
        expect(sendCall.subject).toBe("[TEST] Welcome, Jane Doe!");
        expect(sendCall.to).toBe("admin@test.com");
        expect(sendCall.html).toBe("<html>welcome rendered</html>");

        expect(mockTrackEvent).toHaveBeenCalledWith("TestEmailSent", expect.objectContaining({
            templateName: "welcome",
        }));
    });

    it("derives count from releases array for digest templates", async () => {
        const digestRequest = {
            templateName: "digest",
            recipientEmail: "admin@test.com",
            testData: {
                name: "Jane Doe",
                releases: [
                    { packageName: "react", version: "19.1.0", summary: "New features" },
                    { packageName: "lodash", version: "5.0.0", summary: "ES modules" },
                ],
            },
        };

        mockFindUnique.mockResolvedValue({
            Name: "digest",
            Subject: "Your Digest — {{count}} updates",
            JsxSource: "<digest-jsx/>",
        });
        mockRenderTemplate.mockResolvedValue("<html>digest</html>");
        mockInterpolateSubject.mockReturnValue("Your Digest — 2 updates");
        mockSend.mockResolvedValue({ error: null });

        const result = await sendTestEmail(makeRequest(digestRequest), makeContext());

        expect(result.status).toBe(200);
        expect(mockInterpolateSubject).toHaveBeenCalledWith(
            "Your Digest — {{count}} updates",
            expect.objectContaining({ count: "2" })
        );
    });
});
