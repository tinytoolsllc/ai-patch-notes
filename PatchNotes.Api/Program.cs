using PatchNotes.Data;
using PatchNotes.Api.Middleware;
using PatchNotes.Api.Stytch;
using PatchNotes.Api.Routes;
using PatchNotes.Api.Webhooks;
using PatchNotes.Sync.Core.GitHub;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

// Stytch configuration
var stytchProjectId = builder.Configuration["Stytch:ProjectId"];
var stytchSecret = builder.Configuration["Stytch:Secret"];
var stytchWebhookSecret = builder.Configuration["Stytch:WebhookSecret"];

var missingStytchKeys = new List<string>();
if (string.IsNullOrEmpty(stytchProjectId)) missingStytchKeys.Add("Stytch:ProjectId");
if (string.IsNullOrEmpty(stytchSecret)) missingStytchKeys.Add("Stytch:Secret");
if (string.IsNullOrEmpty(stytchWebhookSecret)) missingStytchKeys.Add("Stytch:WebhookSecret");

if (missingStytchKeys.Count > 0)
{
    throw new InvalidOperationException(
        $"Missing required Stytch configuration: {string.Join(", ", missingStytchKeys)}. " +
        "Please configure these values in appsettings.json or environment variables.");
}

// Stripe configuration — pin to known API version to catch unreviewed SDK upgrades
const string expectedStripeApiVersion = "2026-01-28.clover"; // Stripe.net v50.3.0
if (StripeConfiguration.ApiVersion != expectedStripeApiVersion)
{
    throw new InvalidOperationException(
        $"Stripe API version mismatch: expected {expectedStripeApiVersion}, " +
        $"but SDK reports {StripeConfiguration.ApiVersion}. " +
        "Review Stripe API changelog before updating the pinned version.");
}

var stripeSecretKey = builder.Configuration["Stripe:SecretKey"];
if (string.IsNullOrEmpty(stripeSecretKey))
{
    throw new InvalidOperationException(
        "Missing required Stripe configuration: Stripe:SecretKey. " +
        "Please configure this value in appsettings.json or environment variables.");
}
StripeConfiguration.ApiKey = stripeSecretKey;

builder.Services.Configure<DefaultWatchlistOptions>(builder.Configuration.GetSection(DefaultWatchlistOptions.SectionName));

if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
{
    if (string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    {
        throw new InvalidOperationException(
            "Missing required APPLICATIONINSIGHTS_CONNECTION_STRING configuration. " +
            "Set this value in app settings or environment variables.");
    }
    builder.Services.AddApplicationInsightsTelemetry();
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(options =>
{
    // .NET emits ["integer", "string"] with a regex pattern for int/int? params.
    // Orval generates invalid zod.number().regex() from this. Strip it.
    options.AddSchemaTransformer((schema, context, ct) =>
    {
        if (schema.Type?.HasFlag(Microsoft.OpenApi.JsonSchemaType.Integer) == true && schema.Pattern is not null)
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer;
            schema.Pattern = null;
        }
        return Task.CompletedTask;
    });
});
builder.Services.AddPatchNotesDbContext(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.AddGitHubClient(options =>
{
    var token = builder.Configuration["GitHub:Token"];
    if (!string.IsNullOrEmpty(token))
    {
        options.Token = token;
    }
});

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IStytchClient, StytchClient>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
                  {
                      if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                          return false;
                      var allowedHosts = new HashSet<string> { "app.myreleasenotes.ai", "www.myreleasenotes.ai", "myreleasenotes.ai" };
                      var isAllowed = uri.Scheme == "https" && allowedHosts.Contains(uri.Host);
                      if (builder.Environment.IsDevelopment())
                      {
                          isAllowed = isAllowed
                              || uri.Host == "localhost"
                              || uri.Host.EndsWith(".local")
                              || uri.Host.EndsWith(".devbox.home.arpa");
                      }
                      return isAllowed;
                  })
              .WithHeaders("Content-Type", "X-API-Key", "Accept")
              .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // In production, show error status page for unhandled exceptions
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "text/html";

            var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            var errorMessage = exceptionFeature?.Error?.Message ?? "An unexpected error occurred";

            var html = StatusPageRoutes.GetStatusPageHtml(false, "Unknown", errorMessage);
            await context.Response.WriteAsync(html);
        });
    });
}

app.UseCors();
app.UseMiddleware<CsrfMiddleware>();

// Map routes
app.MapStatusPageRoutes();
app.MapPackageRoutes();
app.MapReleaseRoutes();
app.MapUserRoutes();
app.MapWatchlistRoutes();
app.MapGitHubSearchRoutes();
app.MapSubscriptionRoutes();
app.MapSummaryRoutes();
app.MapFeedRoutes();
app.MapStytchWebhook();
app.MapStripeWebhook();
app.MapEmailTemplateRoutes();
app.MapSitemapRoutes();
app.MapGeoRoutes();

app.Run();

// Make the Program class accessible to the test project
public partial class Program { }
