using PatchNotes.Data;

namespace PatchNotes.Tests;

public class UserTests
{
    private static User MakeUser(string? status, DateTimeOffset? expiresAt = null) => new()
    {
        StytchUserId = "user-test",
        SubscriptionStatus = status,
        SubscriptionExpiresAt = expiresAt,
    };

    [Fact]
    public void IsPro_Active_ReturnsTrue()
    {
        var user = MakeUser("active");
        Assert.True(user.IsPro);
    }

    [Fact]
    public void IsPro_Trialing_ReturnsTrue()
    {
        var user = MakeUser("trialing");
        Assert.True(user.IsPro);
    }

    [Fact]
    public void IsPro_Null_ReturnsFalse()
    {
        var user = MakeUser(null);
        Assert.False(user.IsPro);
    }

    [Fact]
    public void IsPro_PastDue_WithFutureExpiry_ReturnsTrue()
    {
        var user = MakeUser("past_due", DateTimeOffset.UtcNow.AddDays(7));
        Assert.True(user.IsPro);
    }

    [Fact]
    public void IsPro_PastDue_WithExpiredExpiry_ReturnsFalse()
    {
        var user = MakeUser("past_due", DateTimeOffset.UtcNow.AddDays(-1));
        Assert.False(user.IsPro);
    }

    [Fact]
    public void IsPro_PastDue_WithNullExpiry_ReturnsFalse()
    {
        var user = MakeUser("past_due", null);
        Assert.False(user.IsPro);
    }

    [Fact]
    public void IsPro_Canceled_WithFutureExpiry_ReturnsTrue()
    {
        var user = MakeUser("canceled", DateTimeOffset.UtcNow.AddDays(7));
        Assert.True(user.IsPro);
    }

    [Fact]
    public void IsPro_Canceled_WithExpiredExpiry_ReturnsFalse()
    {
        var user = MakeUser("canceled", DateTimeOffset.UtcNow.AddDays(-1));
        Assert.False(user.IsPro);
    }
}
