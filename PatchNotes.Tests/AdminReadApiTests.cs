using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class AdminReadApiTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _authClient = null!;
    private HttpClient _nonAdminClient = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        await _fixture.InitializeAsync();
        _authClient = _fixture.CreateAuthenticatedClient();
        _nonAdminClient = _fixture.CreateNonAdminClient();
    }

    public async Task DisposeAsync()
    {
        _authClient.Dispose();
        _nonAdminClient.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    #region Authorization

    [Theory]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/digest-emails")]
    [InlineData("/api/admin/webhook-events")]
    public async Task AdminReads_GivenNonAdminRequest_ReturnForbidden(string path)
    {
        var response = await _nonAdminClient.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region GET /api/admin/users

    [Fact]
    public async Task GetUsers_ReturnsWatchlistCountWithoutBillingIdentifiers()
    {
        await SeedAsync(db =>
        {
            var user = NewUser("a@test.com", "Alice");
            user.StripeCustomerId = "cus_secret";
            var package = NewPackage("react");
            db.Users.Add(user);
            db.Packages.Add(package);
            db.Watchlists.Add(new Watchlist { UserId = user.Id, PackageId = package.Id });
        });

        var response = await _authClient.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);

        var item = result.GetProperty("items")[0];
        item.GetProperty("email").GetString().Should().Be("a@test.com");
        item.GetProperty("watchlistCount").GetInt32().Should().Be(1);

        // Billing identifiers belong to the detail view, not the browse list.
        item.TryGetProperty("stripeCustomerId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetUsers_GivenSearch_MatchesEmailOrName()
    {
        await SeedAsync(db =>
        {
            db.Users.Add(NewUser("alice@test.com", "Alice"));
            db.Users.Add(NewUser("bob@test.com", "Bob"));
        });

        var byEmail = await _authClient.GetAsync("/api/admin/users?search=alice@");
        var byName = await _authClient.GetAsync("/api/admin/users?search=Bob");

        (await byEmail.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("total").GetInt32().Should().Be(1);
        (await byName.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetUsers_GivenProFilter_ReturnsOnlyActiveSubscribers()
    {
        await SeedAsync(db =>
        {
            var pro = NewUser("pro@test.com", "Pro");
            pro.SubscriptionStatus = "active";
            var lapsed = NewUser("lapsed@test.com", "Lapsed");
            lapsed.SubscriptionStatus = "canceled";
            db.Users.AddRange(pro, lapsed, NewUser("free@test.com", "Free"));
        });

        var response = await _authClient.GetAsync("/api/admin/users?pro=true");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("email").GetString().Should().Be("pro@test.com");
    }

    #endregion

    #region GET /api/admin/users/{id}

    [Fact]
    public async Task GetUser_ReturnsSubscriptionScheduleAndWatchlist()
    {
        string userId = null!;
        await SeedAsync(db =>
        {
            var user = NewUser("a@test.com", "Alice");
            user.StripeCustomerId = "cus_123";
            user.SubscriptionStatus = "active";
            user.EmailDigestEnabled = true;
            user.DigestDay = 5;
            user.DigestHour = 9;
            userId = user.Id;

            var package = NewPackage("react");
            db.Users.Add(user);
            db.Packages.Add(package);
            db.Watchlists.Add(new Watchlist { UserId = user.Id, PackageId = package.Id });
        });

        var response = await _authClient.GetAsync($"/api/admin/users/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("stripeCustomerId").GetString().Should().Be("cus_123");
        body.GetProperty("subscriptionStatus").GetString().Should().Be("active");
        body.GetProperty("digestDay").GetInt32().Should().Be(5);
        body.GetProperty("digestHour").GetInt32().Should().Be(9);

        var watchlist = body.GetProperty("watchlist");
        watchlist.GetArrayLength().Should().Be(1);
        watchlist[0].GetProperty("packageName").GetString().Should().Be("react");
    }

    [Fact]
    public async Task GetUser_GivenUnknownId_ReturnsNotFound()
    {
        var response = await _authClient.GetAsync("/api/admin/users/aaaaaaaaaaaaaaaaaaaaa");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/admin/digest-emails

    [Fact]
    public async Task GetDigestEmails_ReturnsStatusAndErrorWithoutTheRenderedBody()
    {
        await SeedAsync(db =>
        {
            var user = NewUser("a@test.com", "Alice");
            db.Users.Add(user);
            db.SentDigestEmails.Add(new SentDigestEmail
            {
                UserId = user.Id,
                RecipientEmail = "a@test.com",
                Subject = "Your digest",
                Status = "failed",
                ErrorMessage = "Resend rejected the request",
                HtmlBody = new string('x', 5000),
                SentAt = DateTimeOffset.UtcNow,
            });
        });

        var response = await _authClient.GetAsync("/api/admin/digest-emails");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = result.GetProperty("items")[0];
        item.GetProperty("status").GetString().Should().Be("failed");
        item.GetProperty("errorMessage").GetString().Should().Be("Resend rejected the request");

        // The rendered email is stored on every row and would make a page megabytes.
        item.TryGetProperty("htmlBody", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetDigestEmails_GivenStatusFilter_ReturnsOnlyThatStatus()
    {
        await SeedAsync(db =>
        {
            var user = NewUser("a@test.com", "Alice");
            db.Users.Add(user);
            db.SentDigestEmails.Add(NewDigest(user.Id, "sent"));
            db.SentDigestEmails.Add(NewDigest(user.Id, "failed"));
        });

        var response = await _authClient.GetAsync("/api/admin/digest-emails?status=failed");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("status").GetString().Should().Be("failed");
    }

    [Fact]
    public async Task GetDigestEmails_GivenSince_ExcludesOlderRows()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var user = NewUser("a@test.com", "Alice");
            db.Users.Add(user);

            var old = NewDigest(user.Id, "sent");
            old.SentAt = now.AddDays(-10);
            var recent = NewDigest(user.Id, "sent");
            recent.SentAt = now;

            db.SentDigestEmails.AddRange(old, recent);
        });

        var since = Uri.EscapeDataString(now.AddDays(-1).ToString("O"));
        var response = await _authClient.GetAsync($"/api/admin/digest-emails?since={since}");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);
    }

    #endregion

    #region GET /api/admin/webhook-events

    [Fact]
    public async Task GetWebhookEvents_ReturnsTheIdempotencyLedgerNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
            {
                EventId = "evt_old", ProcessedAt = now.AddHours(-2),
            });
            db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
            {
                EventId = "evt_new", ProcessedAt = now,
            });
        });

        var response = await _authClient.GetAsync("/api/admin/webhook-events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(2);
        result.GetProperty("items")[0].GetProperty("eventId").GetString().Should().Be("evt_new");
    }

    [Fact]
    public async Task GetWebhookEvents_ClampsPageSizeToTheMaximum()
    {
        var response = await _authClient.GetAsync("/api/admin/webhook-events?limit=9999");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("limit").GetInt32().Should().Be(200);
    }

    #endregion

    #region Helpers

    private async Task SeedAsync(Action<PatchNotesDbContext> seed)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static User NewUser(string email, string name) => new()
    {
        StytchUserId = $"stytch-{Guid.NewGuid():N}",
        Email = email,
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Package NewPackage(string name) => new()
    {
        Name = name,
        Url = $"https://github.com/owner/{name}",
        GithubOwner = "owner",
        GithubRepo = name,
    };

    private static SentDigestEmail NewDigest(string userId, string status) => new()
    {
        UserId = userId,
        RecipientEmail = "a@test.com",
        Subject = "Your digest",
        Status = status,
        HtmlBody = "<p>body</p>",
        SentAt = DateTimeOffset.UtcNow,
    };

    #endregion
}
