// Telemetry must be imported first to patch HTTP for dependency tracking
import { trackEvent, trackException, flush } from "../lib/telemetry.js";
import { app, InvocationContext, Timer } from "@azure/functions";
import { resend, FROM_ADDRESS, APP_BASE_URL, escapeHtml, emailFooter, sanitizeSubject, isValidEmail } from "../lib/resend.js";
import { getPrismaClient } from "../lib/prisma.js";
import { renderTemplate, interpolateSubject } from "../lib/templateRenderer.js";
import { subDays, getDay, getHours } from "date-fns";
import { UTCDate } from "@date-fns/utc";

const DIGEST_WINDOW_DAYS = 7;

export async function sendDigest(
    myTimer: Timer,
    context: InvocationContext
): Promise<void> {
    const startedAt = Date.now();
    context.log("sendDigest triggered at", new Date().toISOString());

    const now = new UTCDate();
    const currentDayOfWeek = getDay(now);
    const currentHour = getHours(now);
    context.log(`Checking for digests: day=${currentDayOfWeek} hour=${currentHour}`);

    try {
        const db = getPrismaClient();
        const cutoff = subDays(now, DIGEST_WINDOW_DAYS);

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
            const packages: Array<{
                packageName: string;
                releaseCount: number;
                latestVersion: string;
                oldestVersion: string;
                summary: string;
            }> = [];

            for (const watch of user.Watchlists) {
                const releases = watch.Packages.Releases;
                if (releases.length === 0) continue;

                // Releases should be ordered desc by PublishedAt from the query (line 54),
                // but sort by Tag as a safety net in case the query shape changes.
                const sorted = [...releases].sort((a, b) => b.Tag.localeCompare(a.Tag, undefined, { numeric: true }));
                const latestRelease = sorted[0];
                const oldestRelease = sorted[sorted.length - 1];
                const matchingSummary = watch.Packages.ReleaseSummaries.find(
                    (s) => s.MajorVersion === latestRelease.MajorVersion && s.IsPrerelease === latestRelease.IsPrerelease
                );

                packages.push({
                    packageName: watch.Packages.Name,
                    releaseCount: releases.length,
                    latestVersion: latestRelease.Tag,
                    oldestVersion: oldestRelease.Tag,
                    summary: matchingSummary?.Summary ?? "",
                });
            }

            if (packages.length === 0) {
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
                        packages,
                    });
                    subject = interpolateSubject(template.Subject, {
                        name: user.Name ?? "there",
                        count: String(packages.length),
                    });
                } catch (renderErr) {
                    context.warn(`Failed to render digest template for ${user.Email}, using fallback:`, renderErr);
                    html = fallbackDigestHtml(user.Name, packages);
                    subject = sanitizeSubject(`Your Weekly PatchNotes Digest — ${packages.length} packages`);
                }
            } else {
                html = fallbackDigestHtml(user.Name, packages);
                subject = sanitizeSubject(`Your Weekly PatchNotes Digest — ${packages.length} packages`);
            }

            // Email is guaranteed non-null here — validated by isValidEmail above
            const email = user.Email!;
            try {
                const toAddress = user.Name
                    ? `${user.Name} <${email}>`
                    : email;
                const { error } = await resend.emails.send({
                    from: FROM_ADDRESS,
                    to: toAddress,
                    subject,
                    html,
                });

                if (error) {
                    context.error(`Failed to send digest to ${email}:`, error);
                    failures.push({ email, error });
                } else {
                    sentCount++;
                    context.log(`Digest sent to ${email}`);
                }
            } catch (err) {
                context.error(`Error sending to ${email}:`, err);
                trackException(err, { operation: "sendDigest", recipient: email });
                failures.push({ email, error: err });
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
    packages: Array<{ packageName: string; releaseCount: number; latestVersion: string; oldestVersion: string; summary: string }>
): string {
    const packageList = packages
        .map((p) => {
            const versionRange = p.releaseCount === 1
                ? escapeHtml(p.latestVersion)
                : `${escapeHtml(p.oldestVersion)} → ${escapeHtml(p.latestVersion)}`;
            return `<li><strong>${escapeHtml(p.packageName)}</strong> (${p.releaseCount} ${p.releaseCount === 1 ? "release" : "releases"}, ${versionRange}): ${escapeHtml(p.summary)}</li>`;
        })
        .join("\n");

    return `
            <h1>Your Weekly PatchNotes Digest</h1>
            <p>Hi ${escapeHtml(name ?? "there")}, here's what happened this week with the packages you're watching:</p>
            <ul>${packageList}</ul>
            <p><a href="${APP_BASE_URL}">View all updates on PatchNotes</a></p>
            ${emailFooter()}
        `;
}

// Runs every hour, on the hour
app.timer("sendDigest", {
    schedule: "0 0 * * * *",
    handler: sendDigest,
});
