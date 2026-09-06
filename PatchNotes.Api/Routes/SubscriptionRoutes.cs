using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;
using PatchNotes.Api.Stytch;
using Stripe;
using Stripe.Checkout;

namespace PatchNotes.Api.Routes;

public static class SubscriptionRoutes
{
    /// <summary>
    /// Stripe domains that are safe to redirect to.
    /// </summary>
    private static readonly string[] AllowedStripeHosts =
    {
        "checkout.stripe.com",
        "billing.stripe.com",
    };

    private static string GetValidatedOrigin(HttpContext httpContext, IConfiguration configuration)
    {
        var origin = httpContext.Request.Headers.Origin.FirstOrDefault();
        if (!string.IsNullOrEmpty(origin))
        {
            // Trust origins already validated by CsrfMiddleware (AllowedOrigins config).
            var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
            if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                return origin;

            // In development, allow any localhost origin.
            if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Host == "localhost")
                return origin;
        }
        // Fallback to configured base URL
        return configuration["App:BaseUrl"] ?? "https://app.myreleasenotes.ai";
    }

    private static bool IsValidStripeRedirectUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme == "https"
            && AllowedStripeHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase));
    }

    private static IResult SeeOtherRedirect(HttpContext httpContext, string url)
    {
        httpContext.Response.Headers.Location = url;
        return Results.StatusCode(StatusCodes.Status303SeeOther);
    }

    public static WebApplication MapSubscriptionRoutes(this WebApplication app)
    {
        var requireAuth = RouteUtils.CreateAuthFilter();

        var group = app.MapGroup("/api/subscription").WithTags("Subscription");

        // POST /api/subscription/checkout - Create Stripe Checkout session
        group.MapPost("/checkout", async (HttpContext httpContext, PatchNotesDbContext db, IConfiguration configuration) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (string.IsNullOrEmpty(stytchUserId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            var priceId = configuration["Stripe:PriceId"];
            if (string.IsNullOrEmpty(priceId))
            {
                return Results.Problem("Stripe is not configured");
            }

            // Determine the base URL for success/cancel redirects
            var origin = GetValidatedOrigin(httpContext, configuration);

            var sessionOptions = new SessionCreateOptions
            {
                Mode = "subscription",
                AllowPromotionCodes = true,
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Price = priceId,
                        Quantity = 1,
                    },
                },
                SuccessUrl = $"{origin}/subscription-success?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{origin}/subscription-canceled",
                CustomerEmail = user.Email,
                Metadata = new Dictionary<string, string>
                {
                    { "stytch_user_id", stytchUserId },
                    { "app", "patchnotes" },
                },
                // Stripe does not copy the session's metadata onto the subscription it creates,
                // so the tag is set again here. Without it the subscription and its invoices
                // arrive at our webhook carrying nothing that identifies them as ours.
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        { "stytch_user_id", stytchUserId },
                        { "app", "patchnotes" },
                    },
                },
            };

            // If user already has a Stripe customer ID, use it
            if (!string.IsNullOrEmpty(user.StripeCustomerId))
            {
                sessionOptions.Customer = user.StripeCustomerId;
                sessionOptions.CustomerEmail = null; // Can't use both
            }

            var sessionService = new SessionService();
            var session = await sessionService.CreateAsync(sessionOptions);

            if (!IsValidStripeRedirectUrl(session.Url))
            {
                return Results.Problem("Checkout session returned an invalid URL");
            }

            return SeeOtherRedirect(httpContext, session.Url);
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces(StatusCodes.Status303SeeOther)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("CreateCheckoutSession");

        // POST /api/subscription/portal - Create Stripe Customer Portal session
        group.MapPost("/portal", async (HttpContext httpContext, PatchNotesDbContext db, IConfiguration configuration) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (string.IsNullOrEmpty(stytchUserId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            if (string.IsNullOrEmpty(user.StripeCustomerId))
            {
                return Results.BadRequest(new ApiError("No subscription found"));
            }

            var origin = GetValidatedOrigin(httpContext, configuration);

            var portalOptions = new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = user.StripeCustomerId,
                ReturnUrl = $"{origin}/",
            };

            var portalService = new Stripe.BillingPortal.SessionService();
            var session = await portalService.CreateAsync(portalOptions);

            if (!IsValidStripeRedirectUrl(session.Url))
            {
                return Results.Problem("Portal session returned an invalid URL");
            }

            return SeeOtherRedirect(httpContext, session.Url);
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces(StatusCodes.Status303SeeOther)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("CreatePortalSession");

        // GET /api/subscription/status - Get current subscription status
        group.MapGet("/status", async (HttpContext httpContext, PatchNotesDbContext db) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (string.IsNullOrEmpty(stytchUserId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            var session = httpContext.Items["StytchSession"] as StytchSessionResult;
            var isAdmin = session?.IsAdmin ?? false;

            return Results.Ok(new SubscriptionStatusDto
            {
                IsPro = user.IsPro || isAdmin,
                Status = user.SubscriptionStatus,
                ExpiresAt = user.SubscriptionExpiresAt?.ToString("o"),
            });
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces<SubscriptionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetSubscriptionStatus");

        return app;
    }
}

public class SubscriptionStatusDto
{
    public required bool IsPro { get; set; }
    public string? Status { get; set; }
    public string? ExpiresAt { get; set; }
}
