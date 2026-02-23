// Telemetry must be imported first to patch HTTP for dependency tracking
import { trackEvent, trackException, flush } from "../lib/telemetry.js";
import { app, InvocationContext, Timer } from "@azure/functions";
import { resend, FROM_ADDRESS, APP_BASE_URL, escapeHtml, emailFooter, sanitizeSubject, isValidEmail } from "../lib/resend.js";
import { getPrismaClient } from "../lib/prisma.js";
import { renderTemplate, interpolateSubject } from "../lib/templateRenderer.js";

const DIGEST_WINDOW_DAYS = 7;

export async function sendDigest(
    myTimer: Timer,
    context: InvocationContext
): Promise<void> {
    const startedAt = Date.now();
    context.log("sendDigest triggered at", new Date().toISOString());

    const now = new Date();
    const currentDayOfWeek = now.getUTCDay();
    const currentHour = now.getUTCHours();
    context.log(`Checking for digests: day=${currentDayOfWeek} hour=${currentHour}`);

    try {
        const db = getPrismaClient();
        const cutoff = new Date();
        cutoff.setDate(cutoff.getDate() - DIGEST_WINDOW_DAYS);

        const users = await db.users.findMany({
            where: {
                EmailDigestEnabled: true,
                Email: { not: null },
                DigestDay: currentDayOfWeek,
                DigestHour: currentHour,
                Watchlists: {
                    some: {
                        Packages: {
                            Releases: {
                                some: { PublishedAt: { gte: cutoff } },
                            },
                        },
                    },
                },
            },
            select: {
                Email: true,
                Name: true,
                Watchlists: {
                    select: {
                        Packages: {
                            select: {
                                Name: true,
                                Releases: {
                                    where: { PublishedAt: { gte: cutoff } },
                                    orderBy: { PublishedAt: "desc" },
                                    select: {
                                        Tag: true,
                                        MajorVersion: true,
                                        IsPrerelease: true,
                                    },
                                },
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
                },
            },
        });

        if (users.length === 0) {
            context.log("No users with pending digest items");
            trackEvent("DigestCompleted", {
                outcome: "no_users",
                durationMs: (Date.now() - startedAt).toString(),
                usersQueried: "0",
                emailsSent: "0",
                emailsFailed: "0",
                emailsSkipped: "0",
            });
            await flush();
            return;
        }

        context.log(`Sending digests to ${users.length} users`);

        // Fetch digest template once for all users
        const template = await db.emailTemplates.findUnique({ where: { Name: "digest" } });
        if (template) {
            context.log("Using digest template from DB");
        } else {
            context.log("No digest template found in DB, using fallback for all users");
        }

        const failures: Array<{ email: string; error: unknown }> = [];
        let sentCount = 0;
        let skippedCount = 0;

        for (const user of users) {
            const releases: Array<{ packageName: string; version: string; summary: string }> = [];

            for (const watch of user.Watchlists) {
                for (const release of watch.Packages.Releases) {
                    const matchingSummary = watch.Packages.ReleaseSummaries.find(
                        (s) => s.MajorVersion === release.MajorVersion && s.IsPrerelease === release.IsPrerelease
                    );
                    releases.push({
                        packageName: watch.Packages.Name,
                        version: release.Tag,
                        summary: matchingSummary?.Summary ?? "",
                    });
                }
            }

            if (releases.length === 0) {
                skippedCount++;
                continue;
            }

            if (!user.Email || !isValidEmail(user.Email)) {
                context.warn(`Skipping digest for user with invalid email: ${user.Email}`);
                skippedCount++;
                continue;
            }

            let html: string;
            let subject: string;

            if (template) {
                try {
                    html = await renderTemplate(template.JsxSource, {
                        name: user.Name ?? "there",
                        releases,
                    });
                    subject = interpolateSubject(template.Subject, {
                        name: user.Name ?? "there",
                        count: String(releases.length),
                    });
                } catch (renderErr) {
                    context.warn(`Failed to render digest template for ${user.Email}, using fallback:`, renderErr);
                    html = fallbackDigestHtml(user.Name, releases);
                    subject = sanitizeSubject(`Your Weekly PatchNotes Digest — ${releases.length} updates`);
                }
            } else {
                html = fallbackDigestHtml(user.Name, releases);
                subject = sanitizeSubject(`Your Weekly PatchNotes Digest — ${releases.length} updates`);
            }

            try {
                const { error } = await resend.emails.send({
                    from: FROM_ADDRESS,
                    to: user.Email!,
                    subject,
                    html,
                });

                if (error) {
                    context.error(`Failed to send digest to ${user.Email}:`, error);
                    failures.push({ email: user.Email!, error });
                } else {
                    sentCount++;
                    context.log(`Digest sent to ${user.Email}`);
                }
            } catch (err) {
                context.error(`Error sending to ${user.Email}:`, err);
                trackException(err, { operation: "sendDigest", recipient: user.Email! });
                failures.push({ email: user.Email!, error: err });
            }
        }

        context.log(
            `Digest summary: ${sentCount} sent, ${failures.length} failed, ${skippedCount} skipped out of ${users.length} users`
        );

        const outcome = failures.length > 0 ? "partial_failure" : "success";
        trackEvent("DigestCompleted", {
            outcome,
            durationMs: (Date.now() - startedAt).toString(),
            usersQueried: users.length.toString(),
            emailsSent: sentCount.toString(),
            emailsFailed: failures.length.toString(),
            emailsSkipped: skippedCount.toString(),
        });

        if (failures.length > 0) {
            const failedEmails = failures.map((f) => f.email).join(", ");
            const err = new Error(
                `Digest send partially failed: ${failures.length}/${sentCount + failures.length} sends failed. Failed: ${failedEmails}`
            );
            trackException(err, { operation: "sendDigest" });
            await flush();
            throw err;
        }

        await flush();
    } catch (err) {
        trackException(err, { operation: "sendDigest" });
        await flush();
        throw err;
    }
}

function fallbackDigestHtml(
    name: string | null,
    releases: Array<{ packageName: string; version: string; summary: string }>
): string {
    const releaseList = releases
        .map((r) => `<li><strong>${escapeHtml(r.packageName)} ${escapeHtml(r.version)}</strong>: ${escapeHtml(r.summary)}</li>`)
        .join("\n");

    return `
            <h1>Your Weekly PatchNotes Digest</h1>
            <p>Hi ${escapeHtml(name ?? "there")}, here's what happened this week with the packages you're watching:</p>
            <ul>${releaseList}</ul>
            <p><a href="${APP_BASE_URL}">View all updates on PatchNotes</a></p>
            ${emailFooter()}
        `;
}

// Runs every hour, on the hour
app.timer("sendDigest", {
    schedule: "0 0 * * * *",
    handler: sendDigest,
});
