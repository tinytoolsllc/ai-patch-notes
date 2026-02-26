using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PatchNotes.Data;
using PatchNotes.Api.Stytch;

namespace PatchNotes.Tests;

public class PatchNotesApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestSessionToken = "test-session-token-12345";
    public const string TestUserId = "test-user-id";
    public const string NonAdminSessionToken = "non-admin-session-token";
    public const string NonAdminUserId = "non-admin-user-id";

    private readonly MockNpmHandler _npmHandler = new();
    private readonly string _dbName = $"test_{Guid.NewGuid():N}";
    private SqliteConnection? _connection;
    private Action<IWebHostBuilder>? _additionalConfig;
    private Action<IServiceCollection>? _additionalServices;

    public MockNpmHandler NpmHandler => _npmHandler;

    /// <summary>
    /// Register additional configuration to be applied during WebHost setup.
    /// Must be called before accessing Services or CreateClient().
    /// </summary>
    public void ConfigureSettings(Action<IWebHostBuilder> configure)
    {
        _additionalConfig = configure;
    }

    /// <summary>
    /// Register additional service configuration to be applied during WebHost setup.
    /// Must be called before accessing Services or CreateClient().
    /// </summary>
    public void ConfigureServices(Action<IServiceCollection> configure)
    {
        _additionalServices = configure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registrations
            services.RemoveAll<DbContextOptions<PatchNotesDbContext>>();
            services.RemoveAll<DbContextOptions<SqliteContext>>();
            services.RemoveAll<PatchNotesDbContext>();

            // Use a named shared-cache in-memory SQLite database so each DbContext
            // scope gets its own connection (needed for concurrent request tests).
            var connectionString = $"Data Source={_dbName};Mode=Memory;Cache=Shared";
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            services.AddDbContext<PatchNotesDbContext, SqliteContext>(options =>
            {
                options.UseSqlite(connectionString);
                options.AddInterceptors(new SqliteBusyTimeoutInterceptor());
            });

            // Remove existing HttpClientFactory and add mock
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(_npmHandler));

            // Remove existing Stytch client and add mock
            services.RemoveAll<IStytchClient>();
            var mockStytch = new MockStytchClient(TestSessionToken, TestUserId);
            mockStytch.RegisterSession(NonAdminSessionToken, NonAdminUserId, "nonadmin@example.com", []);
            services.AddSingleton<IStytchClient>(mockStytch);

            _additionalServices?.Invoke(services);
        });

        builder.UseSetting("GitHub:Token", "test-github-token");
        builder.UseSetting("AI:ApiKey", "test-ai-key");
        builder.UseSetting("Stytch:ProjectId", "test-project-id");
        builder.UseSetting("Stytch:Secret", "test-secret");
        builder.UseSetting("Stytch:WebhookSecret", "test-webhook-secret");
        builder.UseSetting("Stripe:SecretKey", "sk_test_placeholder");
        builder.UseSetting("AllowedOrigins:0", "http://localhost");

        _additionalConfig?.Invoke(builder);
    }

    public async Task InitializeAsync()
    {
        // Force creation of the WebApplication and database
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public new Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var handler = new SessionCookieHandler(Server.CreateHandler(), TestSessionToken);
        var client = new HttpClient(handler)
        {
            BaseAddress = Server.BaseAddress
        };
        return client;
    }

    public HttpClient CreateNonAdminClient()
    {
        var handler = new SessionCookieHandler(Server.CreateHandler(), NonAdminSessionToken);
        var client = new HttpClient(handler)
        {
            BaseAddress = Server.BaseAddress
        };
        return client;
    }

    /// <summary>
    /// Handler that adds the Stytch session cookie to all requests.
    /// </summary>
    private class SessionCookieHandler : DelegatingHandler
    {
        private readonly string _sessionToken;

        public SessionCookieHandler(HttpMessageHandler inner, string sessionToken) : base(inner)
        {
            _sessionToken = sessionToken;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("Cookie", $"stytch_session={_sessionToken}");
            if (!request.Headers.Contains("Origin"))
            {
                request.Headers.Add("Origin", "http://localhost");
            }
            return base.SendAsync(request, cancellationToken);
        }
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        _npmHandler.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
        }
    }
}

public class MockHttpClientFactory : IHttpClientFactory
{
    private readonly MockNpmHandler _handler;

    public MockHttpClientFactory(MockNpmHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(_handler);
    }
}

public class MockNpmHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Content)> _responses = new();

    public void SetupPackage(string packageName, string githubOwner, string githubRepo)
    {
        var content = $$"""
            {
                "name": "{{packageName}}",
                "repository": {
                    "type": "git",
                    "url": "git+https://github.com/{{githubOwner}}/{{githubRepo}}.git"
                }
            }
            """;
        _responses[$"https://registry.npmjs.org/{packageName}"] = (HttpStatusCode.OK, content);
    }

    public void SetupPackageNotFound(string packageName)
    {
        _responses[$"https://registry.npmjs.org/{packageName}"] = (HttpStatusCode.NotFound, """{"error": "Not found"}""");
    }

    public void SetupPackageWithoutRepo(string packageName)
    {
        var content = $$"""
            {
                "name": "{{packageName}}"
            }
            """;
        _responses[$"https://registry.npmjs.org/{packageName}"] = (HttpStatusCode.OK, content);
    }

    public void Clear()
    {
        _responses.Clear();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";

        if (_responses.TryGetValue(url, out var response))
        {
            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Content, System.Text.Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error": "Not found"}""", System.Text.Encoding.UTF8, "application/json")
        });
    }
}

public class MockStytchClient : IStytchClient
{
    private readonly Dictionary<string, StytchSessionResult> _sessions = new();
    private readonly Dictionary<string, StytchUser> _users = new();

    public MockStytchClient(string validSessionToken, string userId)
    {
        _sessions[validSessionToken] = new StytchSessionResult
        {
            UserId = userId,
            SessionId = "test-session-id",
            Email = "test@example.com",
            Roles = ["patch_notes_admin"]
        };
        _users[userId] = new StytchUser
        {
            UserId = userId,
            Email = "test@example.com",
            Name = "Test User",
            Status = "active"
        };
    }

    public void RegisterSession(string sessionToken, string userId, string email, List<string> roles)
    {
        _sessions[sessionToken] = new StytchSessionResult
        {
            UserId = userId,
            SessionId = $"session-{userId}",
            Email = email,
            Roles = roles,
        };
        _users[userId] = new StytchUser
        {
            UserId = userId,
            Email = email,
            Name = email,
            Status = "active"
        };
    }

    public Task<StytchSessionResult?> AuthenticateSessionAsync(string sessionToken, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionToken, out var session);
        return Task.FromResult(session);
    }

    public Task<StytchUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        _users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }
}

/// <summary>
/// Sets PRAGMA busy_timeout on each new SQLite connection so concurrent writers
/// wait instead of immediately throwing "database is locked".
/// </summary>
internal class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000";
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
