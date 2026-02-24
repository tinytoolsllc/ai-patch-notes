using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PatchNotes.Data;
using PatchNotes.Api.Stytch;

namespace PatchNotes.Api.Routes;

public static class UserRoutes
{
    public static WebApplication MapUserRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        // GET /api/users/me - Get current authenticated user
        group.MapGet("/me", async (HttpContext httpContext, PatchNotesDbContext db) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            var session = httpContext.Items["StytchSession"] as StytchSessionResult;

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);

            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            var isAdmin = session?.IsAdmin ?? false;

            return Results.Ok(new UserDto
            {
                Id = user.Id,
                StytchUserId = user.StytchUserId,
                Email = user.Email,
                Name = user.Name,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsPro = user.IsPro || isAdmin,
                IsAdmin = isAdmin
            });
        })
        .AddEndpointFilterFactory(RouteUtils.CreateAuthFilter())
        .Produces<UserDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetCurrentUser");

        // POST /api/users/login - Create or update user on login (called from frontend after auth)
        group.MapPost("/login", async (
            HttpContext httpContext,
            PatchNotesDbContext db,
            IOptions<DefaultWatchlistOptions> watchlistOptions) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            var email = httpContext.Items["StytchEmail"] as string;
            var session = httpContext.Items["StytchSession"] as StytchSessionResult;
            var isAdmin = session?.IsAdmin ?? false;

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            var isNewUser = user == null;

            if (isNewUser)
            {
                user = new User
                {
                    StytchUserId = stytchUserId!,
                    Email = email,
                    LastLoginAt = DateTimeOffset.UtcNow
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();

                // Auto-populate watchlist with default packages (single batch query)
                var defaults = watchlistOptions.Value.Packages;
                if (defaults.Length > 0)
                {
                    var isPro = user.IsPro || isAdmin;
                    var limit = isPro ? defaults.Length : Math.Min(defaults.Length, WatchlistRoutes.FreeWatchlistLimit);

                    var ownerRepoPairs = defaults
                        .Select(p => p.Split('/', 2))
                        .Where(parts => parts.Length == 2)
                        .Select(parts => parts[0] + "/" + parts[1])
                        .ToList();

                    var matchingPackages = await db.Packages
                        .Where(p => ownerRepoPairs.Contains(p.GithubOwner + "/" + p.GithubRepo))
                        .ToListAsync();

                    foreach (var package in matchingPackages.Take(limit))
                    {
                        db.Watchlists.Add(new Watchlist
                        {
                            UserId = user.Id,
                            PackageId = package.Id,
                        });
                    }

                    await db.SaveChangesAsync();
                }
            }
            else
            {
                user!.Email = email ?? user.Email;
                user.LastLoginAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            return Results.Ok(new UserDto
            {
                Id = user.Id,
                StytchUserId = user.StytchUserId,
                Email = user.Email,
                Name = user.Name,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsPro = user.IsPro || isAdmin,
                IsAdmin = isAdmin
            });
        })
        .AddEndpointFilterFactory(RouteUtils.CreateAuthFilter())
        .Produces<UserDto>(StatusCodes.Status200OK)
        .WithName("LoginUser");

        // PUT /api/users/me - Update current user profile
        // Mirrors the webhook path: fetches user from Stytch API, then updates DB
        group.MapPut("/me", async (HttpContext httpContext, UpdateUserRequest request, PatchNotesDbContext db, IStytchClient stytchClient, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PatchNotes.Api.Routes.UserRoutes");
            var stytchUserId = httpContext.Items["StytchUserId"] as string;

            // Step 1: Fetch fresh user data from Stytch (same as webhook)
            StytchUser? stytchUser = null;
            try
            {
                stytchUser = await stytchClient.GetUserAsync(stytchUserId!);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stytch API call failed for user {StytchUserId}", stytchUserId);
                return Results.Json(new ApiError("Stytch API call failed"), statusCode: 502);
            }

            if (stytchUser == null)
            {
                return Results.Json(new ApiError("Could not fetch user from Stytch"), statusCode: 502);
            }

            // Step 2: Find user in DB (same as webhook)
            User? user;
            try
            {
                user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DB query failed for user {StytchUserId}", stytchUserId);
                return Results.Json(new ApiError("DB query failed"), statusCode: 500);
            }

            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            // Step 3: Update fields (same as webhook, plus the name from request)
            try
            {
                user.Email = stytchUser.Email ?? user.Email;
                user.Name = request.Name ?? stytchUser.Name ?? user.Name;
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DB save failed for user {StytchUserId}", stytchUserId);
                return Results.Json(new ApiError("DB save failed"), statusCode: 500);
            }

            var session = httpContext.Items["StytchSession"] as StytchSessionResult;
            var isAdmin = session?.IsAdmin ?? false;

            return Results.Ok(new UserDto
            {
                Id = user.Id,
                StytchUserId = user.StytchUserId,
                Email = user.Email,
                Name = user.Name,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsPro = user.IsPro || isAdmin,
                IsAdmin = isAdmin
            });
        })
        .AddEndpointFilterFactory(RouteUtils.CreateAuthFilter())
        .Accepts<UpdateUserRequest>("application/json")
        .Produces<UserDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("UpdateCurrentUser");

        // POST /api/users/me/confirm-email-change - Clean up old emails after magic link verification
        group.MapPost("/me/confirm-email-change", async (HttpContext httpContext, PatchNotesDbContext db, IStytchClient stytchClient, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PatchNotes.Api.Routes.UserRoutes");
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            var sessionEmail = httpContext.Items["StytchEmail"] as string;

            // Fetch user from Stytch to get the full emails array
            StytchUser? stytchUser;
            try
            {
                stytchUser = await stytchClient.GetUserAsync(stytchUserId!);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stytch API call failed for user {StytchUserId}", stytchUserId);
                return Results.Json(new ApiError("Stytch API call failed"), statusCode: 502);
            }

            if (stytchUser == null)
            {
                return Results.Json(new ApiError("Could not fetch user from Stytch"), statusCode: 502);
            }

            // If there's only one email, nothing to clean up
            if (stytchUser.Emails.Count <= 1)
            {
                var existingUser = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
                if (existingUser == null) return Results.NotFound(new ApiError("User not found"));

                var session = httpContext.Items["StytchSession"] as StytchSessionResult;
                return Results.Ok(new UserDto
                {
                    Id = existingUser.Id,
                    StytchUserId = existingUser.StytchUserId,
                    Email = existingUser.Email,
                    Name = existingUser.Name,
                    CreatedAt = existingUser.CreatedAt,
                    LastLoginAt = existingUser.LastLoginAt,
                    IsPro = existingUser.IsPro || (session?.IsAdmin ?? false),
                    IsAdmin = session?.IsAdmin ?? false
                });
            }

            // The session email is the new email (the one the user just verified via magic link).
            // Delete all other emails from Stytch.
            var newEmail = sessionEmail ?? stytchUser.Emails.Last().Email;
            var emailsToRemove = stytchUser.Emails.Where(e => e.Email != newEmail).ToList();

            foreach (var oldEmail in emailsToRemove)
            {
                try
                {
                    await stytchClient.DeleteEmailAsync(oldEmail.EmailId);
                    logger.LogInformation("Removed old email {EmailId} ({Email}) from Stytch user {StytchUserId}",
                        oldEmail.EmailId, oldEmail.Email, stytchUserId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete old email {EmailId} from Stytch user {StytchUserId}",
                        oldEmail.EmailId, stytchUserId);
                    return Results.Json(new ApiError("Failed to remove old email from Stytch"), statusCode: 502);
                }
            }

            // Update our DB with the new email
            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            user.Email = newEmail;
            await db.SaveChangesAsync();

            var sess = httpContext.Items["StytchSession"] as StytchSessionResult;
            var isAdmin = sess?.IsAdmin ?? false;

            return Results.Ok(new UserDto
            {
                Id = user.Id,
                StytchUserId = user.StytchUserId,
                Email = user.Email,
                Name = user.Name,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsPro = user.IsPro || isAdmin,
                IsAdmin = isAdmin
            });
        })
        .AddEndpointFilterFactory(RouteUtils.CreateAuthFilter())
        .Produces<UserDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("ConfirmEmailChange");

        // GET /api/users/me/email-preferences - Get current email preferences
        group.MapGet("/me/email-preferences", async (HttpContext httpContext, PatchNotesDbContext db) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;

            var prefs = await db.Users
                .Where(u => u.StytchUserId == stytchUserId)
                .Select(u => new EmailPreferencesDto
                {
                    EmailDigestEnabled = u.EmailDigestEnabled,
                    EmailWelcomeSent = u.EmailWelcomeSent,
                    DigestDay = u.DigestDay,
                    DigestHour = u.DigestHour
                })
                .FirstOrDefaultAsync();

            if (prefs == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            return Results.Ok(prefs);
        })
        .AddEndpointFilterFactory(RouteUtils.CreateAuthFilter())
        .Produces<EmailPreferencesDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetEmailPreferences");

        // PATCH /api/users/me/email-preferences - Update email preferences
        group.MapPatch("/me/email-preferences", async (HttpContext httpContext, UpdateEmailPreferencesRequest request, PatchNotesDbContext db) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            if (request.EmailDigestEnabled.HasValue)
                user.EmailDigestEnabled = request.EmailDigestEnabled.Value;

            if (request.DigestDay.HasValue)
            {
                if (request.DigestDay.Value < 0 || request.DigestDay.Value > 6)
                    return Results.BadRequest(new ApiError("DigestDay must be between 0 (Sunday) and 6 (Saturday)"));
                user.DigestDay = request.DigestDay.Value;
            }

            if (request.DigestHour.HasValue)
            {
                if (request.DigestHour.Value < 0 || request.DigestHour.Value > 23)
                    return Results.BadRequest(new ApiError("DigestHour must be between 0 and 23"));
                user.DigestHour = request.DigestHour.Value;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new EmailPreferencesDto
            {
                EmailDigestEnabled = user.EmailDigestEnabled,
                EmailWelcomeSent = user.EmailWelcomeSent,
                DigestDay = user.DigestDay,
                DigestHour = user.DigestHour
            });
        })
        .AddEndpointFilterFactory(RouteUtils.CreateAuthFilter())
        .Accepts<UpdateEmailPreferencesRequest>("application/json")
        .Produces<EmailPreferencesDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("UpdateEmailPreferences");

        return app;
    }
}

public class UserDto
{
    public required string Id { get; set; }
    public required string StytchUserId { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsPro { get; set; }
    public bool IsAdmin { get; set; }
}

public record UpdateUserRequest(string? Name);

public class EmailPreferencesDto
{
    public bool EmailDigestEnabled { get; set; }
    public bool EmailWelcomeSent { get; set; }
    public int DigestDay { get; set; }
    public int DigestHour { get; set; }
}

public record UpdateEmailPreferencesRequest(bool? EmailDigestEnabled, int? DigestDay, int? DigestHour);
