import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

const { mockSend, mockFindMany, mockFindUnique, mockRenderTemplate, mockInterpolateSubject, mockCreate, mockUpdate } = vi.hoisted(() => ({
    mockSend: vi.fn(),
    mockFindMany: vi.fn(),
    mockFindUnique: vi.fn(),
    mockRenderTemplate: vi.fn(),
    mockInterpolateSubject: vi.fn(),
    mockCreate: vi.fn(),
    mockUpdate: vi.fn(),
}));

vi.mock("../lib/resend", () => ({
    resend: { emails: { send: mockSend } },
    FROM_ADDRESS: "PatchNotes <notifications@patchnotes.dev>",
    APP_BASE_URL: "https://app.myreleasenotes.ai",
    escapeHtml: (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"),
    emailFooter: () => "<footer/>",
    sanitizeSubject: (s: string) => s.replace(/[\r\n]+/g, " ").trim(),
    isValidEmail: (email: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email),
}));

vi.mock("../lib/prisma", () => ({
    getPrismaClient: () => ({
        users: { findMany: mockFindMany },
        emailTemplates: { findUnique: mockFindUnique },
        sentDigestEmails: { create: mockCreate, update: mockUpdate },
    }),
}));

vi.mock("../lib/templateRenderer", () => ({
    renderTemplate: mockRenderTemplate,
    interpolateSubject: mockInterpolateSubject,
}));

import { sendDigest } from "./sendDigest";

function makeContext() {
    return {
        log: vi.fn(),
        warn: vi.fn(),
        error: vi.fn(),
    } as any;
}

function makeTimer(): any {
    return { isPastDue: false };
}

let userIdCounter = 0;
function makeUser(email: string, name: string, releases: Array<{ tag: string; major: number }>) {
    return {
        Id: `user-${++userIdCounter}`,
        Email: email,
        Name: name,
        Watchlists: [
            {
                Packages: {
                    Name: "test-package",
                    Releases: releases.map((r) => ({
                        Tag: r.tag,
                        MajorVersion: r.major,
                        IsPrerelease: false,
                    })),
                    ReleaseSummaries: releases.map((r) => ({
                        Summary: `Summary for ${r.tag}`,
                        MajorVersion: r.major,
                        IsPrerelease: false,
                    })),
                },
            },
        ],
    };
}

describe("sendDigest", () => {
    beforeEach(() => {
        vi.clearAllMocks();
        userIdCounter = 0;
        // Default: no template in DB, use fallback HTML
        mockFindUnique.mockResolvedValue(null);
        // Default: DB operations succeed
        mockCreate.mockResolvedValue({});
        mockUpdate.mockResolvedValue({});
        // Fix the clock so day/hour are deterministic in tests
        vi.useFakeTimers();
        // Wednesday (3) at 14:00 UTC
        vi.setSystemTime(new Date("2026-01-07T14:00:00.000Z"));
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it("returns early when no users have pending items", async () => {
        mockFindMany.mockResolvedValue([]);
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(mockSend).not.toHaveBeenCalled();
        expect(context.log).toHaveBeenCalledWith("No users with pending digest items");
    });

    it("queries only users matching current UTC day and hour", async () => {
        mockFindMany.mockResolvedValue([]);
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(mockFindMany).toHaveBeenCalledWith(
            expect.objectContaining({
                where: expect.objectContaining({
                    DigestDay: 3,  // Wednesday
                    DigestHour: 14,
                }),
            })
        );
    });

    it("logs the current day and hour being checked", async () => {
        mockFindMany.mockResolvedValue([]);
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(context.log).toHaveBeenCalledWith("Checking for digests: day=3 hour=14");
    });

    it("sends digests to all users and logs summary on full success", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
            makeUser("b@test.com", "Bob", [{ tag: "v2.0.0", major: 2 }]),
        ]);
        mockSend.mockResolvedValue({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(mockSend).toHaveBeenCalledTimes(2);
        expect(mockSend).toHaveBeenCalledWith(
            expect.objectContaining({ to: "Alice <a@test.com>" })
        );
        expect(mockSend).toHaveBeenCalledWith(
            expect.objectContaining({ to: "Bob <b@test.com>" })
        );
        expect(context.log).toHaveBeenCalledWith(
            "Digest summary: 2 sent, 0 failed, 0 skipped out of 2 users"
        );
    });

    it("throws when some sends fail with API error", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
            makeUser("b@test.com", "Bob", [{ tag: "v2.0.0", major: 2 }]),
            makeUser("c@test.com", "Carol", [{ tag: "v3.0.0", major: 3 }]),
        ]);
        mockSend
            .mockResolvedValueOnce({ data: { id: "resend-id-123" }, error: null })
            .mockResolvedValueOnce({ error: { message: "rate limited" } })
            .mockResolvedValueOnce({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await expect(sendDigest(makeTimer(), context)).rejects.toThrow(
            "Digest send partially failed: 1/3 sends failed. Failed: b@test.com"
        );
        expect(context.log).toHaveBeenCalledWith(
            "Digest summary: 2 sent, 1 failed, 0 skipped out of 3 users"
        );
    });

    it("throws when sends throw exceptions", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
            makeUser("b@test.com", "Bob", [{ tag: "v2.0.0", major: 2 }]),
        ]);
        mockSend
            .mockResolvedValueOnce({ data: { id: "resend-id-123" }, error: null })
            .mockRejectedValueOnce(new Error("network error"));
        const context = makeContext();

        await expect(sendDigest(makeTimer(), context)).rejects.toThrow(
            "Digest send partially failed: 1/2 sends failed. Failed: b@test.com"
        );
    });

    it("throws when all sends fail", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
            makeUser("b@test.com", "Bob", [{ tag: "v2.0.0", major: 2 }]),
        ]);
        mockSend.mockResolvedValue({ error: { message: "service down" } });
        const context = makeContext();

        await expect(sendDigest(makeTimer(), context)).rejects.toThrow(
            "Digest send partially failed: 2/2 sends failed. Failed: a@test.com, b@test.com"
        );
    });

    it("skips users with invalid emails and counts them", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("valid@test.com", "Valid", [{ tag: "v1.0.0", major: 1 }]),
            makeUser("not-an-email", "Invalid", [{ tag: "v2.0.0", major: 2 }]),
        ]);
        mockSend.mockResolvedValue({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(mockSend).toHaveBeenCalledTimes(1);
        expect(context.warn).toHaveBeenCalledWith(
            "Skipping digest for user with invalid email: not-an-email"
        );
        expect(context.log).toHaveBeenCalledWith(
            "Digest summary: 1 sent, 0 failed, 1 skipped out of 2 users"
        );
    });

    it("skips users with no releases and counts them", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
            makeUser("b@test.com", "Bob", []),  // no releases
        ]);
        mockSend.mockResolvedValue({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(mockSend).toHaveBeenCalledTimes(1);
        expect(context.log).toHaveBeenCalledWith(
            "Digest summary: 1 sent, 0 failed, 1 skipped out of 2 users"
        );
    });

    it("continues sending to remaining users after a failure", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
            makeUser("b@test.com", "Bob", [{ tag: "v2.0.0", major: 2 }]),
            makeUser("c@test.com", "Carol", [{ tag: "v3.0.0", major: 3 }]),
        ]);
        mockSend
            .mockRejectedValueOnce(new Error("fail"))
            .mockResolvedValueOnce({ data: { id: "resend-id-123" }, error: null })
            .mockResolvedValueOnce({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await expect(sendDigest(makeTimer(), context)).rejects.toThrow();
        // All 3 users should have been attempted (not short-circuited)
        expect(mockSend).toHaveBeenCalledTimes(3);
    });

    it("uses DB template when available", async () => {
        const template = {
            Name: "digest",
            Subject: "Digest for {{name}} — {{count}} updates",
            JsxSource: "<fake-jsx/>",
        };
        mockFindUnique.mockResolvedValue(template);
        mockRenderTemplate.mockResolvedValue("<html>rendered</html>");
        mockInterpolateSubject.mockReturnValue("Digest for Alice — 1 updates");
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
        ]);
        mockSend.mockResolvedValue({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(mockRenderTemplate).toHaveBeenCalledWith("<fake-jsx/>", {
            name: "Alice",
            packages: [{ packageName: "test-package", releaseCount: 1, latestVersion: "v1.0.0", oldestVersion: "v1.0.0", summary: "Summary for v1.0.0" }],
        });
        expect(mockInterpolateSubject).toHaveBeenCalledWith(
            "Digest for {{name}} — {{count}} updates",
            { name: "Alice", count: "1" }
        );
        expect(mockSend).toHaveBeenCalledWith(
            expect.objectContaining({
                html: "<html>rendered</html>",
                subject: "Digest for Alice — 1 updates",
            })
        );
    });

    it("groups multiple releases per package into a single entry", async () => {
        mockFindMany.mockResolvedValue([
            {
                Id: "user-inline",
                Email: "a@test.com",
                Name: "Alice",
                Watchlists: [
                    {
                        Packages: {
                            Name: "my-lib",
                            Releases: [
                                { Tag: "v1.3.0", MajorVersion: 1, IsPrerelease: false },
                                { Tag: "v1.2.0", MajorVersion: 1, IsPrerelease: false },
                                { Tag: "v1.1.0", MajorVersion: 1, IsPrerelease: false },
                            ],
                            ReleaseSummaries: [
                                { Summary: "Grouped summary", MajorVersion: 1, IsPrerelease: false },
                            ],
                        },
                    },
                ],
            },
        ]);
        mockSend.mockResolvedValue({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        const sentHtml = mockSend.mock.calls[0][0].html;
        // Should show version range, not individual entries
        expect(sentHtml).toContain("v1.1.0");
        expect(sentHtml).toContain("v1.3.0");
        expect(sentHtml).toContain("3 releases");
        expect(sentHtml).toContain("Grouped summary");
    });

    it("falls back to hardcoded HTML when template rendering fails", async () => {
        mockFindUnique.mockResolvedValue({
            Name: "digest",
            Subject: "Digest",
            JsxSource: "bad-jsx",
        });
        mockRenderTemplate.mockRejectedValue(new Error("render failed"));
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
        ]);
        mockSend.mockResolvedValue({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(context.warn).toHaveBeenCalledWith(
            expect.stringContaining("Failed to render digest template"),
            expect.any(Error)
        );
        expect(mockSend).toHaveBeenCalledTimes(1);
        // Should still send with fallback HTML
        const sentHtml = mockSend.mock.calls[0][0].html;
        expect(sentHtml).toContain("Your Weekly PatchNotes Digest");
    });

    it("inserts a pending SentDigestEmail record before sending", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
        ]);
        mockSend.mockResolvedValue({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(mockCreate).toHaveBeenCalledTimes(1);
        expect(mockCreate).toHaveBeenCalledWith({
            data: expect.objectContaining({
                UserId: "user-1",
                RecipientEmail: "a@test.com",
                Status: "pending",
            }),
        });
    });

    it("updates SentDigestEmail to sent with ResendEmailId on success", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
        ]);
        mockSend.mockResolvedValue({ data: { id: "resend-abc" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        expect(mockUpdate).toHaveBeenCalledTimes(1);
        expect(mockUpdate).toHaveBeenCalledWith({
            where: { Id: expect.any(String) },
            data: { Status: "sent", ResendEmailId: "resend-abc" },
        });
    });

    it("updates SentDigestEmail to failed with ErrorMessage on API error", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
        ]);
        mockSend.mockResolvedValue({ error: { name: "forbidden", message: "domain not verified" } });
        const context = makeContext();

        await expect(sendDigest(makeTimer(), context)).rejects.toThrow();

        expect(mockUpdate).toHaveBeenCalledWith({
            where: { Id: expect.any(String) },
            data: { Status: "failed", ErrorMessage: "domain not verified" },
        });
    });

    it("updates SentDigestEmail to failed on send exception", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
        ]);
        mockSend.mockRejectedValue(new Error("network timeout"));
        const context = makeContext();

        await expect(sendDigest(makeTimer(), context)).rejects.toThrow();

        expect(mockUpdate).toHaveBeenCalledWith({
            where: { Id: expect.any(String) },
            data: { Status: "failed", ErrorMessage: expect.stringContaining("network timeout") },
        });
    });

    it("continues sending even if DB insert fails", async () => {
        mockFindMany.mockResolvedValue([
            makeUser("a@test.com", "Alice", [{ tag: "v1.0.0", major: 1 }]),
            makeUser("b@test.com", "Bob", [{ tag: "v2.0.0", major: 2 }]),
        ]);
        mockCreate.mockRejectedValue(new Error("DB write failed"));
        mockSend.mockResolvedValue({ data: { id: "resend-id-123" }, error: null });
        const context = makeContext();

        await sendDigest(makeTimer(), context);

        // Both emails should still be sent despite DB failures
        expect(mockSend).toHaveBeenCalledTimes(2);
        expect(context.warn).toHaveBeenCalledWith(
            expect.stringContaining("Failed to insert SentDigestEmail"),
            expect.any(Error)
        );
    });
});
