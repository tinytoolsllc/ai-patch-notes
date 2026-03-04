using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Tests;

/// <summary>
/// Verifies that the SqliteContext DateTimeOffset converter fails fast on non-UTC offsets
/// rather than silently losing offset information.
/// </summary>
public class SqliteContextConverterTests : IDisposable
{
    // A single open connection is required for in-memory SQLite — EF opens/closes
    // connections per operation, and each new :memory: connection starts fresh.
    private readonly SqliteConnection _connection;

    public SqliteContextConverterTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private SqliteContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite(_connection)
            .Options;
        var ctx = new SqliteContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task DateTimeOffset_UtcValue_RoundTripsCorrectly()
    {
        var utcNow = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        using var ctx = CreateContext();
        var user = new User { StytchUserId = "test-roundtrip", CreatedAt = utcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var loaded = await ctx.Users.SingleAsync(u => u.StytchUserId == "test-roundtrip");
        loaded.CreatedAt.Should().Be(utcNow);
        loaded.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task DateTimeOffset_NonUtcValue_ThrowsInvalidOperationException()
    {
        var nonUtc = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.FromHours(5));

        using var ctx = CreateContext();
        var user = new User { StytchUserId = "test-non-utc", CreatedAt = nonUtc };
        ctx.Users.Add(user);

        var act = () => ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Non-UTC DateTimeOffset*");
    }
}
