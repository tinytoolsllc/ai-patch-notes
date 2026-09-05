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
    /// The longest <c>Retry-After</c> worth waiting out inside a timer-triggered function.
    /// </summary>
    /// <remarks>
    /// A 429 from an AI provider means one of two very different things. A brief burst limit comes
    /// back in seconds and is worth retrying. An exhausted quota can be hours away, and sitting on
    /// that inside the sync function would block the whole run for a summary that could just as well
    /// wait until next hour. Past this threshold the 429 is allowed to surface, where
    /// SummaryGenerationService stops the run and leaves the work queued.
    /// </remarks>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

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
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        })
        .AddResilienceHandler("ai-rate-limit", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = args =>
                {
                    var response = args.Outcome.Result;
                    if (response?.StatusCode is not (HttpStatusCode.TooManyRequests
                        or HttpStatusCode.ServiceUnavailable))
                    {
                        return ValueTask.FromResult(false);
                    }

                    // Retry a short wait; let a long one through so the run can stop cleanly.
                    var retryAfter = GetRetryAfter(response);
                    return ValueTask.FromResult(retryAfter is null || retryAfter <= MaxRetryAfter);
                },
                DelayGenerator = args =>
                    new ValueTask<TimeSpan?>(GetRetryAfter(args.Outcome.Result)),
            });
        });

        return services;
    }

    /// <summary>
    /// Reads <c>Retry-After</c> in either form the header allows: a delay in seconds, or an
    /// absolute date. Returns null when absent or already in the past.
    /// </summary>
    private static TimeSpan? GetRetryAfter(HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : null;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }
}