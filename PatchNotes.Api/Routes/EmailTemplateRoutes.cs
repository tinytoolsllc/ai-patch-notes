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

        // GET /api/admin/email-templates/{id} - Get single template by id
        group.MapGet("/{id}", async (string id, PatchNotesDbContext db) =>
        {
            var template = await db.EmailTemplates
                .Where(t => t.Id == id)
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

        // PUT /api/admin/email-templates/{id} - Update template
        group.MapPut("/{id}", async (string id, UpdateEmailTemplateRequest request, PatchNotesDbContext db) =>
        {
            var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id);
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

        // POST /api/admin/email-templates/{id}/test - Send a test email
        group.MapPost("/{id}/test", async (
            string id,
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

            var templateExists = await db.EmailTemplates.AnyAsync(t => t.Id == id, cancellationToken);
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

            if (string.IsNullOrEmpty(emailFunctionKey))
            {
                logger.LogError("EmailFunction:Key is not configured");
                return Results.Json(
                    new ApiError("Email function key not configured"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                using var http = httpClientFactory.CreateClient();
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, emailFunctionUrl);

                httpRequest.Headers.Add("x-functions-key", emailFunctionKey);

                var payload = new
                {
                    templateId = id,
                    recipientEmail = request.RecipientEmail,
                    testData = request.TestData
                };

                string jsonPayload;
                try
                {
                    jsonPayload = JsonSerializer.Serialize(payload);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to serialize test email payload");
                    return Results.Json(
                        new ApiError("Failed to serialize test email payload", ex.Message),
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                httpRequest.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

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
                    new ApiError("Failed to call email function", ex.Message),
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
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("EmailTemplateRoutes");
            var renderUrl = configuration["EmailFunction:PreviewUrl"];
            if (string.IsNullOrEmpty(renderUrl))
            {
                return Results.StatusCode(503);
            }

            var functionKey = configuration["EmailFunction:Key"];
            if (string.IsNullOrEmpty(functionKey))
            {
                logger.LogError("EmailFunction:Key is not configured");
                return Results.Json(
                    new ApiError("Email function key not configured"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            using var http = httpClientFactory.CreateClient();
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, renderUrl);

            httpRequest.Headers.Add("x-functions-key", functionKey);

            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(new { jsxSource = request.JsxSource, props = request.Props }),
                System.Text.Encoding.UTF8,
                "application/json");

            try
            {
                var response = await http.SendAsync(httpRequest, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

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
                return Results.Json(
                    new ApiError("Failed to call email render function", ex.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .AddEndpointFilterFactory(requireAuth)
        .AddEndpointFilterFactory(requireAdmin)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status502BadGateway)
        .WithName("PreviewEmailTemplate");

        return app;
    }

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

public record SendTestEmailRequest(string RecipientEmail, JsonElement? TestData);

public record PreviewTemplateRequest(string JsxSource, Dictionary<string, object>? Props);
