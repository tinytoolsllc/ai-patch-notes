// Telemetry must be imported first to patch HTTP for dependency tracking
import { trackEvent, trackException, flush } from "../lib/telemetry.js";
import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";
import { resend, FROM_ADDRESS, sanitizeSubject, isValidEmail } from "../lib/resend.js";
import { getPrismaClient } from "../lib/prisma.js";
import { renderTemplate, interpolateSubject } from "../lib/templateRenderer.js";
import { subDays } from "date-fns";
import { UTCDate } from "@date-fns/utc";
import { nanoid } from "../lib/nanoid.js";

interface SendTestEmailRequest {
  templateId: string;
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
  context: InvocationContext,
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
        (s) => s.MajorVersion === r.MajorVersion && s.IsPrerelease === r.IsPrerelease,
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
  context: InvocationContext,
): Promise<HttpResponseInit> {
  const startedAt = Date.now();
  context.log("sendTestEmail triggered");

  let body: SendTestEmailRequest;
  try {
    body = (await request.json()) as SendTestEmailRequest;
  } catch {
    return { status: 400, body: "Invalid JSON body" };
  }

  if (!body.templateId) {
    return { status: 400, body: "Missing required field: templateId" };
  }

  if (!body.recipientEmail) {
    return { status: 400, body: "Missing required field: recipientEmail" };
  }

  if (!isValidEmail(body.recipientEmail)) {
    return { status: 400, body: "Invalid email address format" };
  }

  try {
    const db = getPrismaClient();
    const template = await db.emailTemplates.findUnique({ where: { Id: body.templateId } });

    if (!template) {
      return { status: 404, body: `Template not found: ${body.templateId}` };
    }

    // Use provided testData or build from real DB data. The narrowing happens on
    // `body.testData` itself rather than through a separate boolean, because the
    // compiler cannot carry a type guard across an intermediate variable -- under
    // strictNullChecks the old form left templateData possibly undefined even
    // though it never is at runtime.
    const providedData =
      body.testData && typeof body.testData === "object" ? body.testData : undefined;
    const useRealData = providedData === undefined;
    const templateData =
      providedData ?? (await buildRealData(db, template.Name, body.recipientEmail, context));

    context.log(`Using ${useRealData ? "real" : "sample"} data for test email`);

    // Render HTML using the production pipeline — no fallback
    const html = await renderTemplate(template.JsxSource, templateData);

    // Build subject interpolation vars by stringifying templateData values
    const subjectVars: Record<string, string> = {};
    for (const [key, value] of Object.entries(templateData)) {
      // templateData holds `unknown`, and some entries are arrays of objects
      // (digest `releases`, for one). A subject line is plain text, so only
      // primitives are interpolated -- stringifying an object would put
      // "[object Object]" in the subject. Skipped keys interpolate to "",
      // which is what interpolateSubject does for anything it cannot find.
      if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
        subjectVars[key] = String(value);
      }
    }
    // Derive count from releases array for digest templates
    const releases = templateData.releases;
    if (Array.isArray(releases)) {
      subjectVars.count = String(releases.length);
    }

    const subject = sanitizeSubject("[TEST] " + interpolateSubject(template.Subject, subjectVars));

    // Look up user ID for the recipient (may not exist)
    const recipientUser = await db.users.findFirst({
      where: { Email: body.recipientEmail },
      select: { Id: true },
    });

    // Insert pending record before attempting send
    const sentEmailId = nanoid();
    if (recipientUser) {
      try {
        await db.sentDigestEmails.create({
          data: {
            Id: sentEmailId,
            UserId: recipientUser.Id,
            Subject: subject,
            HtmlBody: html,
            RecipientEmail: body.recipientEmail,
            Status: "pending",
            SentAt: new Date(),
          },
        });
      } catch (dbErr) {
        context.warn("Failed to insert SentDigestEmail record:", dbErr);
      }
    }

    const { data, error } = await resend.emails.send({
      from: FROM_ADDRESS,
      to: body.recipientEmail,
      subject,
      html,
    });

    if (error) {
      context.error("Resend error:", error);
      if (recipientUser) {
        try {
          await db.sentDigestEmails.update({
            where: { Id: sentEmailId },
            data: { Status: "failed", ErrorMessage: (error.message ?? "").slice(0, 1024) },
          });
        } catch (dbErr) {
          context.warn("Failed to update SentDigestEmail status:", dbErr);
        }
      }
      trackEvent("TestEmailFailed", {
        reason: "resend_error",
        templateId: body.templateId,
        errorMessage: error.message,
        durationMs: (Date.now() - startedAt).toString(),
      });
      await flush();
      return { status: 500, body: `Failed to send email: ${error.message}` };
    }

    if (recipientUser) {
      try {
        await db.sentDigestEmails.update({
          where: { Id: sentEmailId },
          data: { Status: "sent", ResendEmailId: data?.id ?? null },
        });
      } catch (dbErr) {
        context.warn("Failed to update SentDigestEmail status:", dbErr);
      }
    }

    trackEvent("TestEmailSent", {
      templateId: body.templateId,
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
      templateId: body.templateId,
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
