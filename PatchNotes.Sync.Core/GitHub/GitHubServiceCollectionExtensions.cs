using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace PatchNotes.Sync.Core.GitHub;

/// <summary>
/// Extension methods for registering GitHub client services.
/// </summary>
public static class GitHubServiceCollectionExtensions
{
    /// <summary>
    /// Adds the GitHub client to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGitHubClient(
        this IServiceCollection services,
        Action<GitHubClientOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddOptions<GitHubClientOptions>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<RateLimitHandler>();

        services.AddHttpClient<IGitHubClient, GitHubClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GitHubClientOptions>>().Value;

            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            if (!string.IsNullOrWhiteSpace(options.Token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
            }
        })
        .AddHttpMessageHandler<RateLimitHandler>()
        .AddResilienceHandler("github-resilience", builder =>
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
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.InternalServerError or
                        HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout);
                },
                DelayGenerator = args =>
                {
                    var retryAfter = args.Outcome.Result?.Headers.RetryAfter?.Delta;
                    return new ValueTask<TimeSpan?>(retryAfter);
                }
            });
        });

        return services;
    }
}
