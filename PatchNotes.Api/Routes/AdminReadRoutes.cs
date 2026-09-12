using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Api.Routes;

/// <summary>
/// Read-only admin views over data that has no endpoint at all today.
/// </summary>
/// <remarks>
/// Users, sent digests and processed webhook events are only reachable through the database right
/// now. Releases and packages are not here because /api/releases and /api/packages already serve
/// them; the point of this file is missing capability, not a parallel admin surface.
/// </remarks>
public static class AdminReadRoutes
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    private static (int Take, int Skip) Page(int? limit, int? offset) =>
        (Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), Math.Max(offset ?? 0, 0));

    public static WebApplication MapAdminReadRoutes(this WebApplication app)
    {
        var requireAuth = RouteUtils.CreateAuthFilter();
        var requireAdmin = RouteUtils.CreateAdminFilter();

        MapUsers(app, requireAuth, requireAdmin);
        MapDigestEmails(app, requireAuth, requireAdmin);
        MapWebhookEvents(app, requireAuth, requireAdmin);

        return app;
    }

    private static void MapUsers(
        WebApplication app,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> requireAuth,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> requireAdmin)
    {
        var group = app.MapGroup("/api/admin/users")
            .WithTags("AdminUsers")
            .AddEndpointFilterFactory(requireAuth)
            .AddEndpointFilterFactory(requireAdmin);

        // GET /api/admin/users — find a user. Only /api/users/me exists today, so answering
        // "which account is this?" means opening the database.
        //
        // Stripe identifiers are deliberately absent here and present on the detail endpoint. A
        // list is browsed; a detail view is asked for. Keeping billing identifiers off the browse
        // path costs nothing and narrows where they appear.
        group.MapGet("/", async (
            string? search, bool? pro, string? sort, int? limit, int? offset, PatchNotesDbContext db) =>
        {
            var (take, skip) = Page(limit, offset);

            var query = db.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(u =>
                    (u.Email != null && u.Email.Contains(term))
                    || (u.Name != null && u.Name.Contains(term)));
            }

            if (pro == true)
            {
                query = query.Where(u => u.SubscriptionStatus == "active");
            }

            query = sort?.ToLowerInvariant() switch
            {
                "lastloginat" => query.OrderByDescending(u => u.LastLoginAt),
                _ => query.OrderByDescending(u => u.CreatedAt),
            };

            var total = await query.CountAsync();

            var items = await query
                .Skip(skip)
                .Take(take)
                .Select(u => new AdminUserDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    Name = u.Name,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt,
                    SubscriptionStatus = u.SubscriptionStatus,
                    EmailDigestEnabled = u.EmailDigestEnabled,
                    WatchlistCount = u.Watchlists.Count,
                })
                .ToListAsync();

            return Results.Ok(new PaginatedResponse<AdminUserDto>
            {
                Items = items,
                Total = total,
                Limit = take,
                Offset = skip,
            });
        })
        .Produces<PaginatedResponse<AdminUserDto>>(StatusCodes.Status200OK)
        .WithName("GetAdminUsers");

        // GET /api/admin/users/{id} — everything needed to answer a support question about one
        // account: billing state, digest schedule, and what they are actually watching.
        group.MapGet("/{id:length(21)}", async (string id, PatchNotesDbContext db) =>
        {
            var user = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => new AdminUserDetailDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    Name = u.Name,
                    StytchUserId = u.StytchUserId,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt,
                    LastLoginAt = u.LastLoginAt,
                    SubscriptionStatus = u.SubscriptionStatus,
                    SubscriptionExpiresAt = u.SubscriptionExpiresAt,
                    StripeCustomerId = u.StripeCustomerId,
                    StripeSubscriptionId = u.StripeSubscriptionId,
                    EmailDigestEnabled = u.EmailDigestEnabled,
                    DigestDay = u.DigestDay,
                    DigestHour = u.DigestHour,
                    EmailWelcomeSent = u.EmailWelcomeSent,
                    Watchlist = u.Watchlists
                        .Select(w => new AdminUserWatchlistItemDto
                        {
                            PackageId = w.PackageId,
                            PackageName = w.Package.Name,
                            GithubOwner = w.Package.GithubOwner,
                            GithubRepo = w.Package.GithubRepo,
                        })
                        .ToList(),
                })
                .FirstOrDefaultAsync();

            return user == null
                ? Results.NotFound(new ApiError("User not found"))
                : Results.Ok(user);
        })
        .Produces<AdminUserDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetAdminUser");
    }

    private static void MapDigestEmails(
        WebApplication app,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> requireAuth,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> requireAdmin)
    {
        var group = app.MapGroup("/api/admin/digest-emails")
            .WithTags("AdminDigestEmails")
            .AddEndpointFilterFactory(requireAuth)
            .AddEndpointFilterFactory(requireAdmin);

        // GET /api/admin/digest-emails — did the digest go out, and to whom.
        //
        // HtmlBody is stored on every row and is never returned here. A page of fifty rendered
        // emails would be megabytes, and the question this endpoint answers -- sent or failed, and
        // why -- is answered by the status and error columns.
        group.MapGet("/", async (
            string? userId, string? status, DateTimeOffset? since,
            int? limit, int? offset, PatchNotesDbContext db) =>
        {
            var (take, skip) = Page(limit, offset);

            var query = db.SentDigestEmails.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(e => e.UserId == userId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var wanted = status.Trim();
                query = query.Where(e => e.Status == wanted);
            }

            if (since.HasValue)
            {
                query = query.Where(e => e.SentAt >= since.Value);
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(e => e.SentAt)
                .Skip(skip)
                .Take(take)
                .Select(e => new AdminDigestEmailDto
                {
                    Id = e.Id,
                    UserId = e.UserId,
                    RecipientEmail = e.RecipientEmail,
                    Subject = e.Subject,
                    Status = e.Status,
                    ResendEmailId = e.ResendEmailId,
                    ErrorMessage = e.ErrorMessage,
                    SentAt = e.SentAt,
                })
                .ToListAsync();

            return Results.Ok(new PaginatedResponse<AdminDigestEmailDto>
            {
                Items = items,
                Total = total,
                Limit = take,
                Offset = skip,
            });
        })
        .Produces<PaginatedResponse<AdminDigestEmailDto>>(StatusCodes.Status200OK)
        .WithName("GetAdminDigestEmails");
    }

    private static void MapWebhookEvents(
        WebApplication app,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> requireAuth,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> requireAdmin)
    {
        var group = app.MapGroup("/api/admin/webhook-events")
            .WithTags("AdminWebhookEvents")
            .AddEndpointFilterFactory(requireAuth)
            .AddEndpointFilterFactory(requireAdmin);

        // GET /api/admin/webhook-events — the idempotency ledger.
        //
        // Each row means "this event was already handled, do not handle it again". When a webhook
        // appears to have been ignored, the question is whether it arrived and was deduplicated or
        // never arrived at all, and this is the only way to tell the two apart.
        group.MapGet("/", async (
            DateTimeOffset? since, int? limit, int? offset, PatchNotesDbContext db) =>
        {
            var (take, skip) = Page(limit, offset);

            var query = db.ProcessedWebhookEvents.AsNoTracking();

            if (since.HasValue)
            {
                query = query.Where(e => e.ProcessedAt >= since.Value);
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(e => e.ProcessedAt)
                .Skip(skip)
                .Take(take)
                .Select(e => new AdminWebhookEventDto
                {
                    EventId = e.EventId,
                    EventType = e.EventType,
                    ProcessedAt = e.ProcessedAt,
                })
                .ToListAsync();

            return Results.Ok(new PaginatedResponse<AdminWebhookEventDto>
            {
                Items = items,
                Total = total,
                Limit = take,
                Offset = skip,
            });
        })
        .Produces<PaginatedResponse<AdminWebhookEventDto>>(StatusCodes.Status200OK)
        .WithName("GetAdminWebhookEvents");
    }
}

public class AdminUserDto
{
    public required string Id { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? SubscriptionStatus { get; set; }
    public bool EmailDigestEnabled { get; set; }
    public int WatchlistCount { get; set; }
}

public class AdminUserDetailDto
{
    public required string Id { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public required string StytchUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public string? SubscriptionStatus { get; set; }
    public DateTimeOffset? SubscriptionExpiresAt { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    public bool EmailDigestEnabled { get; set; }

    /// <summary>Day of week the digest is scheduled for, matching sendDigest's UTC comparison.</summary>
    public int DigestDay { get; set; }
    public int DigestHour { get; set; }
    public bool EmailWelcomeSent { get; set; }

    public required List<AdminUserWatchlistItemDto> Watchlist { get; set; }
}

public class AdminUserWatchlistItemDto
{
    public required string PackageId { get; set; }
    public required string PackageName { get; set; }
    public required string GithubOwner { get; set; }
    public required string GithubRepo { get; set; }
}

public class AdminDigestEmailDto
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string RecipientEmail { get; set; }
    public required string Subject { get; set; }

    /// <summary>pending, sent or failed.</summary>
    public required string Status { get; set; }

    public string? ResendEmailId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset SentAt { get; set; }
}

public class AdminWebhookEventDto
{
    public required string EventId { get; set; }

    /// <summary>Null for rows recorded before the column existed; it cannot be backfilled.</summary>
    public string? EventType { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
