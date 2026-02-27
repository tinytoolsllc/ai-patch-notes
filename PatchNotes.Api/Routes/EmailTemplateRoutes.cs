using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Api.Routes;

public static class EmailTemplateRoutes
{
    public static WebApplication MapEmailTemplateRoutes(this WebApplication app)
    {
        var requireAuth = RouteUtils.CreateAuthFilter();
        var requireAdmin = RouteUtils.CreateAdminFilter();

        var group = app.MapGroup("/api/admin/email-templates").WithTags("EmailTemplates");

        // GET /api/admin/email-templates - List all templates
        group.MapGet("/", async (PatchNotesDbContext db) =>
        {
            // Seed defaults on first access
            if (!await db.EmailTemplates.AnyAsync())
            {
                await SeedDefaultTemplatesAsync(db);
            }

            var templates = await db.EmailTemplates
                .OrderBy(t => t.Name)
                .Select(t => new EmailTemplateDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Subject = t.Subject,
                    JsxSource = t.JsxSource,
                    UpdatedAt = t.UpdatedAt,
                })
                .ToListAsync();

            return Results.Ok(templates);
        })
        .AddEndpointFilterFactory(requireAuth)
        .AddEndpointFilterFactory(requireAdmin)
        .Produces<List<EmailTemplateDto>>(StatusCodes.Status200OK)
        .WithName("GetEmailTemplates");

        // GET /api/admin/email-templates/{name} - Get single template by name
        group.MapGet("/{name}", async (string name, PatchNotesDbContext db) =>
        {
            var template = await db.EmailTemplates
                .Where(t => t.Name == name)
                .Select(t => new EmailTemplateDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Subject = t.Subject,
                    JsxSource = t.JsxSource,
                    UpdatedAt = t.UpdatedAt,
                })
                .FirstOrDefaultAsync();

            if (template == null)
            {
                return Results.NotFound(new ApiError("Template not found"));
            }

            return Results.Ok(template);
        })
        .AddEndpointFilterFactory(requireAuth)
        .AddEndpointFilterFactory(requireAdmin)
        .Produces<EmailTemplateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetEmailTemplate");

        // PUT /api/admin/email-templates/{name} - Update template
        group.MapPut("/{name}", async (string name, UpdateEmailTemplateRequest request, PatchNotesDbContext db) =>
        {
            var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Name == name);
            if (template == null)
            {
                return Results.NotFound(new ApiError("Template not found"));
            }

            if (request.Subject != null)
            {
                template.Subject = request.Subject;
            }

            if (request.JsxSource != null)
            {
                template.JsxSource = request.JsxSource;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new EmailTemplateDto
            {
                Id = template.Id,
                Name = template.Name,
                Subject = template.Subject,
                JsxSource = template.JsxSource,
                UpdatedAt = template.UpdatedAt,
            });
        })
        .AddEndpointFilterFactory(requireAuth)
        .AddEndpointFilterFactory(requireAdmin)
        .Produces<EmailTemplateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("UpdateEmailTemplate");

        // POST /api/admin/email-templates/{name}/test - Send a test email
        group.MapPost("/{name}/test", async (
            string name,
            SendTestEmailRequest request,
            PatchNotesDbContext db,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("PatchNotes.Api.Routes.EmailTemplateRoutes");

            if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            {
                return Results.BadRequest(new ApiError("Recipient email is required"));
            }

            var templateExists = await db.EmailTemplates.AnyAsync(t => t.Name == name, cancellationToken);
            if (!templateExists)
            {
                return Results.NotFound(new ApiError("Template not found"));
            }

            var emailFunctionUrl = configuration["EmailFunction:Url"];
            var emailFunctionKey = configuration["EmailFunction:Key"];

            if (string.IsNullOrEmpty(emailFunctionUrl))
            {
                return Results.Json(
                    new ApiError("Email function URL not configured"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                using var http = httpClientFactory.CreateClient();
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, emailFunctionUrl);

                if (!string.IsNullOrEmpty(emailFunctionKey))
                    httpRequest.Headers.Add("x-functions-key", emailFunctionKey);

                var payload = new
                {
                    templateName = name,
                    recipientEmail = request.RecipientEmail,
                    testData = request.TestData
                };

                httpRequest.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await http.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Email function returned {StatusCode}: {Body}",
                        response.StatusCode, responseBody);
                    return Results.Json(
                        new ApiError("Email function error", responseBody),
                        statusCode: (int)response.StatusCode);
                }

                return Results.NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to call email function for test email");
                return Results.Json(
                    new ApiError("Failed to send test email"),
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .AddEndpointFilterFactory(requireAuth)
        .AddEndpointFilterFactory(requireAdmin)
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .WithName("SendTestEmail");

        // POST /api/admin/email-templates/preview - Render a template with sample data
        group.MapPost("/preview", async (
            PreviewTemplateRequest request,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("EmailTemplateRoutes");
            var renderUrl = configuration["EmailFunction:PreviewUrl"];
            if (string.IsNullOrEmpty(renderUrl))
            {
                return Results.StatusCode(503);
            }

            using var http = httpClientFactory.CreateClient();
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, renderUrl);

            var functionKey = configuration["EmailFunction:Key"];
            if (!string.IsNullOrEmpty(functionKey))
                httpRequest.Headers.Add("x-functions-key", functionKey);

            var payload = JsonSerializer.Serialize(new { jsxSource = request.JsxSource, props = request.Props });
            httpRequest.Content = new StringContent(payload);
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            try
            {
                var response = await http.SendAsync(httpRequest);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Email function render failed ({Status}): {Body}", (int)response.StatusCode, body);
                    return Results.Problem(body, statusCode: (int)response.StatusCode);
                }

                return Results.Ok(new { html = body });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to call email render function");
                return Results.StatusCode(502);
            }
        })
        .AddEndpointFilterFactory(requireAuth)
        .AddEndpointFilterFactory(requireAdmin)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status502BadGateway)
        .WithName("PreviewEmailTemplate");

        return app;
    }

    /// <summary>
    /// Seeds default email templates if none exist.
    /// </summary>
    public static async Task SeedDefaultTemplatesAsync(PatchNotesDbContext db)
    {
        if (await db.EmailTemplates.AnyAsync())
            return;

        var templates = new[]
        {
            new EmailTemplate
            {
                Name = "welcome",
                Subject = "Welcome to PatchNotes, {{name}}!",
                JsxSource = WelcomeJsx,
            },
            new EmailTemplate
            {
                Name = "digest",
                Subject = "Your Weekly PatchNotes Digest — {{count}} packages",
                JsxSource = DigestJsx,
            },
        };

        db.EmailTemplates.AddRange(templates);
        await db.SaveChangesAsync();
    }

    private const string WelcomeJsx = """
        import { Html, Head, Body, Container, Section, Heading, Text, Hr, Link } from "@react-email/components";

        export default function WelcomeEmail({ name = "there" }) {
          return (
            <Html>
              <Head />
              <Body style={{ backgroundColor: "#f6f9fc", fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif" }}>
                <Container style={{ backgroundColor: "#ffffff", margin: "0 auto", padding: "40px 20px", maxWidth: "560px" }}>
                  <Section>
                    <Heading style={{ color: "#1a1a1a", fontSize: "24px", fontWeight: "bold", margin: "0 0 16px" }}>
                      Welcome to PatchNotes, {name}!
                    </Heading>
                    <Text style={{ color: "#4a4a4a", fontSize: "16px", lineHeight: "26px" }}>
                      You're all set to receive release notifications for the packages you care about.
                    </Text>
                    <Text style={{ color: "#4a4a4a", fontSize: "16px", lineHeight: "26px" }}>
                      Head to your <Link href="https://app.myreleasenotes.ai" style={{ color: "#5469d4" }}>dashboard</Link> to start watching packages.
                    </Text>
                  </Section>
                  <Hr style={{ borderColor: "#e6ebf1", margin: "32px 0" }} />
                  <Text style={{ color: "#8898aa", fontSize: "12px" }}>
                    PatchNotes — Release notifications for developers
                  </Text>
                  <Text style={{ color: "#8898aa", fontSize: "12px" }}>
                    <Link href="https://myreleasenotes.ai/settings" style={{ color: "#8898aa", textDecoration: "underline" }}>Manage email preferences</Link>
                  </Text>
                </Container>
              </Body>
            </Html>
          );
        }
        """;

    private const string DigestJsx = """
        import { Html, Head, Body, Container, Section, Heading, Text, Hr, Link } from "@react-email/components";

        export default function DigestEmail({ name = "there", packages = [] }) {
          return (
            <Html>
              <Head />
              <Body style={{ backgroundColor: "#f6f9fc", fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif" }}>
                <Container style={{ backgroundColor: "#ffffff", margin: "0 auto", padding: "40px 20px", maxWidth: "560px" }}>
                  <Section>
                    <Heading style={{ color: "#1a1a1a", fontSize: "24px", fontWeight: "bold", margin: "0 0 16px" }}>
                      Your Weekly PatchNotes Digest
                    </Heading>
                    <Text style={{ color: "#4a4a4a", fontSize: "16px", lineHeight: "26px" }}>
                      Hi {name}, here's what happened this week with the packages you're watching:
                    </Text>
                    {packages.length > 0 ? (
                      <ul style={{ padding: "0 0 0 20px" }}>
                        {packages.map((p, i) => (
                          <li key={i} style={{ color: "#4a4a4a", fontSize: "16px", lineHeight: "26px", marginBottom: "8px" }}>
                            <strong>{p.packageName}</strong>{" "}
                            ({p.releaseCount} {p.releaseCount === 1 ? "release" : "releases"},{" "}
                            {p.releaseCount === 1 ? p.latestVersion : `${p.oldestVersion} → ${p.latestVersion}`}): {p.summary}
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <Text style={{ color: "#4a4a4a", fontSize: "16px" }}>No new releases this week.</Text>
                    )}
                    <Text style={{ color: "#4a4a4a", fontSize: "16px", lineHeight: "26px" }}>
                      <Link href="https://app.myreleasenotes.ai" style={{ color: "#5469d4" }}>View all updates on PatchNotes</Link>
                    </Text>
                  </Section>
                  <Hr style={{ borderColor: "#e6ebf1", margin: "32px 0" }} />
                  <Text style={{ color: "#8898aa", fontSize: "12px" }}>
                    PatchNotes — Release notifications for developers
                  </Text>
                  <Text style={{ color: "#8898aa", fontSize: "12px" }}>
                    <Link href="https://myreleasenotes.ai/settings" style={{ color: "#8898aa", textDecoration: "underline" }}>Manage email preferences</Link>
                  </Text>
                </Container>
              </Body>
            </Html>
          );
        }
        """;
}

public class EmailTemplateDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Subject { get; set; }
    public required string JsxSource { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public record UpdateEmailTemplateRequest(string? Subject, string? JsxSource);

public record SendTestEmailRequest(string RecipientEmail, JsonElement TestData);

public record PreviewTemplateRequest(string JsxSource, Dictionary<string, object>? Props);
