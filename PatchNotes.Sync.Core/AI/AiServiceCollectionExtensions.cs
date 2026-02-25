using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace PatchNotes.Sync.Core.AI;

/// <summary>
/// Extension methods for registering AI client services.
/// </summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Adds the AI client to the service collection.
    /// Supports any OpenAI-compatible API provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAiClient(
        this IServiceCollection services,
        Action<AiClientOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddOptions<AiClientOptions>();
        }

        services.AddHttpClient<IAiClient, AiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AiClientOptions>>().Value;

            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(120);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        })
        .AddResilienceHandler("ai-resilience", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is HttpRequestException)
                        return ValueTask.FromResult(true);
                    return ValueTask.FromResult(args.Outcome.Result?.StatusCode is
                        HttpStatusCode.InternalServerError or
                        HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout);
                }
            });
        });

        return services;
    }
}
