using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stytch.net.Clients;
using Stytch.net.Models;
using Stytch.net.Models.Consumer;

namespace PatchNotes.Api.Stytch;

/// <summary>
/// Client for interacting with the Stytch API using the official SDK.
/// </summary>
public class StytchClient : IStytchClient
{
    private readonly ConsumerClient _client;
    private readonly ILogger<StytchClient> _logger;

    public StytchClient(IConfiguration configuration, ILogger<StytchClient> logger)
    {
        _logger = logger;
        _client = new ConsumerClient(new ClientConfig
        {
            ProjectId = configuration["Stytch:ProjectId"],
            ProjectSecret = configuration["Stytch:Secret"]
        });
    }

    public async Task<StytchSessionResult?> AuthenticateSessionAsync(string sessionToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Sessions.Authenticate(
                new SessionsAuthenticateRequest
                {
                    SessionToken = sessionToken
                });

            return new StytchSessionResult
            {
                UserId = response.Session.UserId,
                SessionId = response.Session.SessionId,
                Email = response.User.Emails?.FirstOrDefault()?.Email,
                Roles = response.User.Roles?.ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stytch session authentication failed");
            return null;
        }
    }

    public async Task<StytchUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Users.Get(new UsersGetRequest(userId));

            string? name = null;
            if (response.Name != null)
            {
                var nameParts = new[] { response.Name.FirstName, response.Name.LastName }
                    .Where(n => !string.IsNullOrEmpty(n));
                name = string.Join(" ", nameParts);
                if (string.IsNullOrEmpty(name)) name = null;
            }

            return new StytchUser
            {
                UserId = response.UserId,
                Email = response.Emails?.FirstOrDefault()?.Email,
                Emails = response.Emails?.Select(e => new StytchEmail
                {
                    EmailId = e.EmailId,
                    Email = e.Email,
                    Verified = e.Verified
                }).ToList() ?? [],
                Name = name,
                Status = response.Status
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stytch get user failed for user {UserId}", userId);
            return null;
        }
    }

    public async Task DeleteEmailAsync(string emailId, CancellationToken cancellationToken = default)
    {
        await _client.Users.DeleteEmail(new UsersDeleteEmailRequest(emailId));
    }
}
