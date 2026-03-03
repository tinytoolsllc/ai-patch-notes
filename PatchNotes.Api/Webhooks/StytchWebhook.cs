using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;
using PatchNotes.Api.Stytch;

namespace PatchNotes.Api.Webhooks;

/// <summary>
/// Webhook event from Stytch (via Svix).
/// </summary>
public record StytchWebhookEvent(
    string action,
    string id,
    string object_type
);

public static class StytchWebhook
{
    public static WebApplication MapStytchWebhook(this WebApplication app)
    {
        // POST /webhooks/stytch - Handle Stytch webhook events
        app.MapPost("/webhooks/stytch", async (HttpContext httpContext, PatchNotesDbContext db, IStytchClient stytchClient, IConfiguration configuration, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PatchNotes.Api.Webhooks.StytchWebhook");

            // CRITICAL: Fail early if webhook secret is not configured
            var stytchWebhookSecret = configuration["Stytch:WebhookSecret"];
            if (string.IsNullOrEmpty(stytchWebhookSecret))
            {
                logger.LogError("Stytch:WebhookSecret is not configured. Rejecting webhook to prevent unverified payloads");
                return Results.StatusCode(503);
            }

            // Read Svix headers for signature verification
            var svixId = httpContext.Request.Headers["svix-id"].FirstOrDefault();
            var svixTimestamp = httpContext.Request.Headers["svix-timestamp"].FirstOrDefault();
            var svixSignature = httpContext.Request.Headers["svix-signature"].FirstOrDefault();

            // Read the raw body for signature verification
            using var reader = new StreamReader(httpContext.Request.Body);
            var body = await reader.ReadToEndAsync();

            // Verify webhook signature using Svix
            if (string.IsNullOrEmpty(svixId) || string.IsNullOrEmpty(svixTimestamp) || string.IsNullOrEmpty(svixSignature))
            {
                return Results.Unauthorized();
            }

            var svix = new Svix.Webhook(stytchWebhookSecret);
            var headers = new WebHeaderCollection();
            headers.Set("svix-id", svixId);
            headers.Set("svix-timestamp", svixTimestamp);
            headers.Set("svix-signature", svixSignature);

            try
            {
                svix.Verify(body, headers);
            }
            catch
            {
                return Results.Unauthorized();
            }

            // Idempotency: skip already-processed events (svix-id is stable across retries)
            if (await db.ProcessedWebhookEvents.AnyAsync(e => e.EventId == svixId))
            {
                logger.LogInformation("Skipping already-processed Stytch event {SvixId}", svixId);
                return Results.Ok(new { received = true, duplicate = true });
            }

            // Step 1: Deserialize the webhook payload
            StytchWebhookEvent? stytchEvent;
            try
            {
                stytchEvent = JsonSerializer.Deserialize<StytchWebhookEvent>(body);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Stytch webhook JSON parse error: {Message}", ex.Message);
                return Results.BadRequest(new { error = "Invalid webhook payload" });
            }

            if (stytchEvent == null)
            {
                return Results.BadRequest(new { error = "Invalid webhook payload" });
            }

            logger.LogInformation("Stytch webhook received: object_type={ObjectType}, action={Action}", stytchEvent.object_type, stytchEvent.action);

            // Handle different Stytch webhook events based on object_type and action
            // IMPORTANT: Don't trust data in the webhook payload - fetch fresh data from Stytch API
            switch (stytchEvent)
            {
                case { object_type: "user", action: "CREATE" or "UPDATE" }:
                    {
                        // Step 2: Fetch fresh user data from Stytch API
                        StytchUser? stytchUser;
                        try
                        {
                            stytchUser = await stytchClient.GetUserAsync(stytchEvent.id);
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug("Stytch API call failed for user {UserId}: {Message}", stytchEvent.id, ex.Message);
                            return Results.Json(new { error = "Failed to fetch user data" }, statusCode: 502);
                        }

                        if (stytchUser == null)
                        {
                            logger.LogDebug("Stytch API returned null for user {UserId}", stytchEvent.id);
                            return Results.Json(new { error = "Failed to fetch user data" }, statusCode: 502);
                        }

                        logger.LogDebug("Stytch user fetched: userId={UserId}, email={Email}, name={Name}", stytchUser.UserId, stytchUser.Email, stytchUser.Name);

                        // Step 3: Find or create user in DB
                        User? user;
                        try
                        {
                            user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchEvent.id);
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug("DB query failed for StytchUserId={UserId}: {Message}", stytchEvent.id, ex.Message);
                            return Results.Json(new { error = "Internal server error" }, statusCode: 500);
                        }

                        if (user == null)
                        {
                            logger.LogDebug("Creating new user for StytchUserId={UserId}", stytchEvent.id);
                            user = new User
                            {
                                StytchUserId = stytchUser.UserId,
                                Email = stytchUser.Email,
                                Name = stytchUser.Name,
                            };
                            db.Users.Add(user);
                        }
                        else
                        {
                            logger.LogDebug("Updating existing user {UserId} for StytchUserId={StytchUserId}", user.Id, stytchEvent.id);
                            user.Email = stytchUser.Email ?? user.Email;
                            user.Name = stytchUser.Name ?? user.Name;
                        }

                        // Step 4: Save to DB
                        try
                        {
                            await db.SaveChangesAsync();
                            logger.LogDebug("DB save succeeded for StytchUserId={UserId}", stytchEvent.id);
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug("DB save failed for StytchUserId={UserId}: {Message} | Inner: {InnerMessage}", stytchEvent.id, ex.Message, ex.InnerException?.Message);
                            return Results.Json(new { error = "Internal server error" }, statusCode: 500);
                        }

                        break;
                    }

                case { object_type: "user", action: "DELETE" }:
                    {
                        try
                        {
                            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchEvent.id);
                            if (user != null)
                            {
                                db.Users.Remove(user);
                                await db.SaveChangesAsync();
                                logger.LogDebug("Deleted user for StytchUserId={UserId}", stytchEvent.id);
                            }
                            else
                            {
                                logger.LogDebug("Delete webhook: no user found for StytchUserId={UserId}", stytchEvent.id);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug("Delete user failed for StytchUserId={UserId}: {Message}", stytchEvent.id, ex.Message);
                            return Results.Json(new { error = "Internal server error" }, statusCode: 500);
                        }
                        break;
                    }

                default:
                    logger.LogInformation("Unhandled Stytch webhook: object_type={ObjectType}, action={Action}", stytchEvent.object_type, stytchEvent.action);
                    break;
            }

            // Record event as processed for idempotency
            db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
            {
                EventId = svixId,
                ProcessedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            return Results.Ok(new { received = true });
        });

        return app;
    }
}
