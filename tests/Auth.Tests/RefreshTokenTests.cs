using Auth.Domain.Entities;

namespace Auth.Tests;

public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan Cap = TimeSpan.FromDays(30);

    [Fact]
    public void StartFamily_Expires_After_The_Lifetime_And_Sets_The_Family()
    {
        var userId = Guid.NewGuid();

        var token = RefreshToken.StartFamily(userId, "hash", Now, Lifetime, Cap);

        Assert.Equal(Now.AddDays(7), token.ExpiresAtUtc);
        Assert.Equal(Now, token.CreatedAtUtc);
        Assert.Equal(Now, token.FamilyStartedAtUtc);
        Assert.Equal(userId, token.UserId);
        Assert.Equal("hash", token.TokenHash);
        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.NotEqual(Guid.Empty, token.FamilyId);
        Assert.NotEqual(token.Id, token.FamilyId);
        Assert.False(token.IsRevoked);
        Assert.Null(token.ReplacedById);
    }

    [Fact]
    public void Each_Family_Gets_A_New_Family_Id()
    {
        var userId = Guid.NewGuid();

        var first = RefreshToken.StartFamily(userId, "a", Now, Lifetime, Cap);
        var second = RefreshToken.StartFamily(userId, "b", Now, Lifetime, Cap);

        Assert.NotEqual(first.FamilyId, second.FamilyId);
    }

    [Fact]
    public void CreateSuccessor_Keeps_The_Family_And_Its_Start()
    {
        var first = RefreshToken.StartFamily(Guid.NewGuid(), "a", Now, Lifetime, Cap);
        var later = Now.AddDays(3);

        var successor = first.CreateSuccessor("b", later, Lifetime, Cap);

        Assert.NotNull(successor);
        Assert.Equal(first.FamilyId, successor.FamilyId);
        Assert.Equal(first.FamilyStartedAtUtc, successor.FamilyStartedAtUtc);
        Assert.Equal(first.UserId, successor.UserId);
        Assert.NotEqual(first.Id, successor.Id);
        Assert.Equal("b", successor.TokenHash);
        Assert.Equal(later, successor.CreatedAtUtc);
    }

    [Fact]
    public void CreateSuccessor_Slides_For_A_Young_Family()
    {
        var first = RefreshToken.StartFamily(Guid.NewGuid(), "a", Now, Lifetime, Cap);
        var later = Now.AddDays(3);

        var successor = first.CreateSuccessor("b", later, Lifetime, Cap);

        Assert.Equal(later.AddDays(7), successor!.ExpiresAtUtc);
    }

    [Fact]
    public void CreateSuccessor_Is_Capped_For_An_Old_Family()
    {
        var first = RefreshToken.StartFamily(Guid.NewGuid(), "a", Now, Lifetime, Cap);
        var later = Now.AddDays(26);

        var successor = first.CreateSuccessor("b", later, Lifetime, Cap);

        Assert.Equal(Now.AddDays(30), successor!.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void CreateSuccessor_Returns_Null_At_Or_Past_The_Cap(int days)
    {
        var first = RefreshToken.StartFamily(Guid.NewGuid(), "a", Now, Lifetime, Cap);

        Assert.Null(first.CreateSuccessor("b", Now.AddDays(days), Lifetime, Cap));
    }

    [Fact]
    public void CreateSuccessor_Returns_Null_When_The_Cap_Is_Lowered_Below_The_Family_Age()
    {
        var first = RefreshToken.StartFamily(Guid.NewGuid(), "a", Now, Lifetime, Cap);

        Assert.Null(first.CreateSuccessor("b", Now.AddDays(10), Lifetime, TimeSpan.FromDays(9)));
    }

    [Fact]
    public void IsExpired_Is_True_At_The_Expiry_Instant()
    {
        var token = RefreshToken.StartFamily(Guid.NewGuid(), "a", Now, Lifetime, Cap);

        Assert.False(token.IsExpired(token.ExpiresAtUtc.AddTicks(-1)));
        Assert.True(token.IsExpired(token.ExpiresAtUtc));
        Assert.True(token.IsExpired(token.ExpiresAtUtc.AddTicks(1)));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-1, 30)]
    [InlineData(7, 0)]
    [InlineData(7, -1)]
    public void Non_Positive_Spans_Throw(int lifetimeDays, int capDays)
    {
        var lifetime = TimeSpan.FromDays(lifetimeDays);
        var cap = TimeSpan.FromDays(capDays);
        var first = RefreshToken.StartFamily(Guid.NewGuid(), "a", Now, Lifetime, Cap);

        Assert.Throws<ArgumentOutOfRangeException>(() => RefreshToken.StartFamily(Guid.NewGuid(), "a", Now, lifetime, cap));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.CreateSuccessor("b", Now, lifetime, cap));
    }

    [Fact]
    public void No_Property_Holds_A_Raw_Token()
    {
        // Only the hash is stored; nothing on the entity is named for the secret itself.
        var names = typeof(RefreshToken).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains("TokenHash", names);
        Assert.DoesNotContain(names, n => n is "Token" or "RawToken" or "Value" or "Secret");
    }
}
