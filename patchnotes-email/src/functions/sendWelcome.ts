// Telemetry must be imported first to patch HTTP for dependency tracking
import { trackEvent, trackException, flush } from "../lib/telemetry.js";
import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";
import {
  resend,
  FROM_ADDRESS,
  escapeHtml,
  emailFooter,
  sanitizeSubject,
  isValidEmail,
} from "../lib/resend.js";
import { getPrismaClient } from "../lib/prisma.js";
import { renderTemplate, interpolateSubject } from "../lib/templateRenderer.js";

interface WelcomeRequest {
  email: string;
  name: string;
}

export async function sendWelcome(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  const startedAt = Date.now();
  context.log("sendWelcome triggered");

  let body: WelcomeRequest;
  try {
    body = (await request.json()) as WelcomeRequest;
  } catch {
    return { status: 400, body: "Invalid JSON body" };
  }

  if (!body.email || !body.name) {
    return { status: 400, body: "Missing required fields: email, name" };
  }

  if (!isValidEmail(body.email)) {
    return { status: 400, body: "Invalid email address format" };
  }

  try {
    let html: string;
    let subject: string;
    let templateSource = "fallback";

    const db = getPrismaClient();
    const template = await db.emailTemplates.findUnique({ where: { Name: "welcome" } });

    if (template) {
      try {
        html = await renderTemplate(template.JsxSource, { name: body.name });
        subject = interpolateSubject(template.Subject, { name: body.name });
        templateSource = "database";
        context.log("Rendered welcome email from DB template");
      } catch (renderErr) {
        context.warn("Failed to render welcome template, using fallback:", renderErr);
        html = fallbackWelcomeHtml(body.name);
        subject = sanitizeSubject(`Welcome to PatchNotes, ${body.name}!`);
      }
    } else {
      context.log("No welcome template found in DB, using fallback");
      html = fallbackWelcomeHtml(body.name);
      subject = sanitizeSubject(`Welcome to PatchNotes, ${body.name}!`);
    }

    const { error } = await resend.emails.send({
      from: FROM_ADDRESS,
      to: body.email,
      subject,
      html,
    });

    if (error) {
      context.error("Resend error:", error);
      trackEvent("WelcomeEmailFailed", {
        reason: "resend_error",
        errorMessage: error.message,
        durationMs: (Date.now() - startedAt).toString(),
      });
      await flush();
      return { status: 500, body: `Failed to send email: ${error.message}` };
    }

    trackEvent("WelcomeEmailSent", {
      templateSource,
      durationMs: (Date.now() - startedAt).toString(),
    });
    await flush();
    return { status: 200, body: "Welcome email sent" };
  } catch (err) {
    context.error("Unexpected error:", err);
    trackException(err, { operation: "sendWelcome", recipient: body.email });
    trackEvent("WelcomeEmailFailed", {
      reason: "exception",
      durationMs: (Date.now() - startedAt).toString(),
    });
    await flush();
    return { status: 500, body: "Internal server error" };
  }
}

function fallbackWelcomeHtml(name: string): string {
  const escaped = escapeHtml(name);
  return `
            <h1>Welcome to PatchNotes, ${escaped}!</h1>
            <p>You're all set to receive release notifications for the packages you care about.</p>
            <p>Head to your dashboard to start watching packages.</p>
            ${emailFooter()}
        `;
}

app.http("sendWelcome", {
  methods: ["POST"],
  authLevel: "function",
  handler: sendWelcome,
});
