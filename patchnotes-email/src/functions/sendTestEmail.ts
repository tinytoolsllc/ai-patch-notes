// Telemetry must be imported first to patch HTTP for dependency tracking
import { trackEvent, trackException, flush } from "../lib/telemetry.js";
import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";
import { resend, FROM_ADDRESS, sanitizeSubject, isValidEmail } from "../lib/resend.js";
import { getPrismaClient } from "../lib/prisma.js";
import { renderTemplate, interpolateSubject } from "../lib/templateRenderer.js";
import { subDays } from "date-fns";
import { UTCDate } from "@date-fns/utc";

interface SendTestEmailRequest {
    templateName: string;
    recipientEmail: string;
    testData?: Record<string, unknown>;
}

/**
 * Build template props from real database data when testData is not provided.
 */
async function buildRealData(
    db: ReturnType<typeof getPrismaClient>,
    templateName: string,
    recipientEmail: string,
    context: InvocationContext
): Promise<Record<string, unknown>> {
    if (templateName === "welcome") {
        // Look up the user's name by email, fall back to "there"
        const user = await db.users.findFirst({
            where: { Email: recipientEmail },
            select: { Name: true },
        });
        return { name: user?.Name ?? "there" };
    }

    if (templateName === "digest") {
        const user = await db.users.findFirst({
            where: { Email: recipientEmail },
            select: { Name: true },
        });

        const cutoff = subDays(new UTCDate(), 7);
        const recentReleases = await db.releases.findMany({
            where: { PublishedAt: { gte: cutoff } },
            orderBy: { PublishedAt: "desc" },
            take: 10,
            select: {
                Tag: true,
                MajorVersion: true,
                IsPrerelease: true,
                Packages: {
                    select: {
                        Name: true,
                        ReleaseSummaries: {
                            select: {
                                Summary: true,
                                MajorVersion: true,
                                IsPrerelease: true,
                            },
                        },
                    },
                },
            },
        });

        const releases = recentReleases.map((r) => {
            const summary = r.Packages.ReleaseSummaries.find(
                (s) => s.MajorVersion === r.MajorVersion && s.IsPrerelease === r.IsPrerelease
            );
            return {
                packageName: r.Packages.Name,
                version: r.Tag,
                summary: summary?.Summary ?? "",
            };
        });

        context.log(`Built real digest data: ${releases.length} releases from last 7 days`);
        return { name: user?.Name ?? "there", releases };
    }

    // Unknown template — return empty props
    return {};
}

export async function sendTestEmail(
    request: HttpRequest,
    context: InvocationContext
): Promise<HttpResponseInit> {
    const startedAt = Date.now();
    context.log("sendTestEmail triggered");

    let body: SendTestEmailRequest;
    try {
        body = (await request.json()) as SendTestEmailRequest;
    } catch {
        return { status: 400, body: "Invalid JSON body" };
    }

    if (!body.templateName) {
        return { status: 400, body: "Missing required field: templateName" };
    }

    if (!body.recipientEmail) {
        return { status: 400, body: "Missing required field: recipientEmail" };
    }

    if (!isValidEmail(body.recipientEmail)) {
        return { status: 400, body: "Invalid email address format" };
    }

    try {
        const db = getPrismaClient();
        const template = await db.emailTemplates.findUnique({ where: { Name: body.templateName } });

        if (!template) {
            return { status: 404, body: `Template not found: ${body.templateName}` };
        }

        // Use provided testData or build from real DB data
        const useRealData = !body.testData || typeof body.testData !== "object";
        const templateData = useRealData
            ? await buildRealData(db, body.templateName, body.recipientEmail, context)
            : body.testData;

        context.log(`Using ${useRealData ? "real" : "sample"} data for test email`);

        // Render HTML using the production pipeline — no fallback
        const html = await renderTemplate(template.JsxSource, templateData);

        // Build subject interpolation vars by stringifying templateData values
        const subjectVars: Record<string, string> = {};
        for (const [key, value] of Object.entries(templateData)) {
            subjectVars[key] = String(value);
        }
        // Derive count from releases array for digest templates
        const releases = templateData.releases;
        if (Array.isArray(releases)) {
            subjectVars.count = String(releases.length);
        }

        const subject = sanitizeSubject("[TEST] " + interpolateSubject(template.Subject, subjectVars));

        const { error } = await resend.emails.send({
            from: FROM_ADDRESS,
            to: body.recipientEmail,
            subject,
            html,
        });

        if (error) {
            context.error("Resend error:", error);
            trackEvent("TestEmailFailed", {
                reason: "resend_error",
                templateName: body.templateName,
                errorMessage: error.message,
                durationMs: (Date.now() - startedAt).toString(),
            });
            await flush();
            return { status: 500, body: `Failed to send email: ${error.message}` };
        }

        trackEvent("TestEmailSent", {
            templateName: body.templateName,
            dataSource: useRealData ? "database" : "sample",
            durationMs: (Date.now() - startedAt).toString(),
        });
        await flush();
        return { status: 200, body: "Test email sent" };
    } catch (err) {
        context.error("Unexpected error:", err);
        trackException(err, { operation: "sendTestEmail", recipient: body.recipientEmail });
        trackEvent("TestEmailFailed", {
            reason: "exception",
            templateName: body.templateName,
            durationMs: (Date.now() - startedAt).toString(),
        });
        await flush();
        return { status: 500, body: "Internal server error" };
    }
}

app.http("sendTestEmail", {
    methods: ["POST"],
    authLevel: "function",
    handler: sendTestEmail,
});
