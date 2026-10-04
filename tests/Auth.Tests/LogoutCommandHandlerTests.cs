using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auth.Tests;

public class LogoutCommandHandlerTests
{
    private const string Presented = "presented-token";

    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IRefreshTokenHasher> _hasher = new();

    public LogoutCommandHandlerTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns((string t) => "hash-of-" + t);
    }

    [Fact]
    public async Task A_Current_Token_Revokes_Its_Family_Once()
    {
        var stored = Family(Now.AddDays(-1));

        var result = await CreateSubject(stored).Handle(new LogoutCommand(Presented), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Rotated_Token_Revokes_The_Family()
    {
        var stored = Family(Now.AddDays(-1));
        Revoke(stored);

        var result = await CreateSubject(stored).Handle(new LogoutCommand(Presented), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Expired_Token_Revokes_The_Family()
    {
        var stored = Family(Now.AddDays(-20));

        var result = await CreateSubject(stored).Handle(new LogoutCommand(Presented), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Already_Revoked_And_Expired_Token_Still_Succeeds()
    {
        var stored = Family(Now.AddDays(-20));
        Revoke(stored);

        var result = await CreateSubject(stored).Handle(new LogoutCommand(Presented), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_Revoke_Is_Keyed_On_The_Family_Only()
    {
        var stored = Family(Now.AddDays(-1));
        var otherFamily = Family(Now.AddDays(-1));

        await CreateSubject(stored).Handle(new LogoutCommand(Presented), CancellationToken.None);

        _refreshTokens.Verify(r => r.RevokeFamilyAsync(It.Is<Guid>(id => id != stored.FamilyId), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotEqual(stored.FamilyId, otherFamily.FamilyId);
    }

    [Fact]
    public async Task An_Unknown_Token_Succeeds_Without_A_Write()
    {
        var result = await CreateSubject(null).Handle(new LogoutCommand(Presented), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_Empty_Or_Whitespace_Token_Succeeds_Without_Touching_The_Repository(string token)
    {
        var result = await CreateSubject(null).Handle(new LogoutCommand(token), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _refreshTokens.VerifyNoOtherCalls();
        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task An_Over_Long_Token_Succeeds_Without_Touching_The_Repository()
    {
        var result = await CreateSubject(null).Handle(new LogoutCommand(new string('a', 257)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _refreshTokens.VerifyNoOtherCalls();
        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_Null_Token_Is_400_With_No_Repository_Call()
    {
        var result = await CreateSubject(null).Handle(new LogoutCommand(null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        Assert.Equal("Invalid request: Refresh token is required.", result.Error.Details);
        _refreshTokens.VerifyNoOtherCalls();
    }

    private LogoutCommandHandler CreateSubject(RefreshToken? stored)
    {
        _refreshTokens.Setup(r => r.GetByHashAsync("hash-of-" + Presented, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        _refreshTokens.Setup(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new LogoutCommandHandler(
            _refreshTokens.Object,
            _hasher.Object,
            new LogoutCommandValidator(),
            new FixedTimeProvider(new DateTimeOffset(Now)),
            NullLogger<LogoutCommandHandler>.Instance);
    }

    private static RefreshToken Family(DateTime issuedAtUtc) =>
        RefreshToken.StartFamily(Guid.NewGuid(), "hash-of-" + Presented, issuedAtUtc, TimeSpan.FromDays(7), TimeSpan.FromDays(30));

    // The entity has no Revoke method on purpose (revocation is a conditional statement in the repository).
    private static void Revoke(RefreshToken token) =>
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.RevokedAtUtc))!.SetValue(token, Now.AddHours(-1));
}
