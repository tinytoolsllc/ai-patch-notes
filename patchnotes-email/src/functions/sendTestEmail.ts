// Telemetry must be imported first to patch HTTP for dependency tracking
import { trackEvent, trackException, flush } from "../lib/telemetry.js";
import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";
import { resend, FROM_ADDRESS, sanitizeSubject, isValidEmail } from "../lib/resend.js";
import { getPrismaClient } from "../lib/prisma.js";
import { renderTemplate, interpolateSubject } from "../lib/templateRenderer.js";

interface SendTestEmailRequest {
    templateName: string;
    recipientEmail: string;
    testData: Record<string, unknown>;
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

    if (!body.testData || typeof body.testData !== "object") {
        return { status: 400, body: "Missing required field: testData" };
    }

    try {
        const db = getPrismaClient();
        const template = await db.emailTemplates.findUnique({ where: { Name: body.templateName } });

        if (!template) {
            return { status: 404, body: `Template not found: ${body.templateName}` };
        }

        // Render HTML using the production pipeline — no fallback
        const html = await renderTemplate(template.JsxSource, body.testData);

        // Build subject interpolation vars by stringifying testData values
        const subjectVars: Record<string, string> = {};
        for (const [key, value] of Object.entries(body.testData)) {
            subjectVars[key] = String(value);
        }
        // Derive count from releases array for digest templates
        const releases = body.testData.releases;
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
