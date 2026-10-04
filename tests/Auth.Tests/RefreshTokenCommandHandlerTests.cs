using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Application.Models;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmartAppointments.BuildingBlocks.Enums;

namespace Auth.Tests;

public class RefreshTokenCommandHandlerTests
{
    private const string Presented = "presented-token";
    private const string Successor = "successor-token";
    private const string InvalidMessage = "Invalid or expired refresh token.";

    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan Cap = TimeSpan.FromDays(30);
    private static readonly DateTime AccessExpiry = new(2026, 10, 4, 13, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITokenGenerator> _tokenGenerator = new();
    private readonly Mock<IRefreshTokenHasher> _hasher = new();
    private readonly CapturingLogger _logger = new();
    private readonly List<string> _calls = [];

    public RefreshTokenCommandHandlerTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns((string t) => "hash-of-" + t);
        _tokenGenerator.Setup(t => t.GenerateRefreshToken()).Returns(Successor);
        _tokenGenerator.Setup(t => t.GenerateAccessToken(It.IsAny<User>()))
            .Callback(() => _calls.Add("access"))
            .Returns(new AccessToken("access-token", AccessExpiry));
        _refreshTokens.Setup(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("rotate"))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task A_Valid_Token_Returns_A_New_Pair_And_Rotates()
    {
        var user = CreateUser(UserRole.Staff);
        var stored = StartFamily(user, Now.AddDays(-3));
        Arrange(stored, user);
        RefreshToken? rotatedTo = null;
        _refreshTokens.Setup(r => r.TryRotateAsync(stored.Id, It.IsAny<RefreshToken>(), Now, It.IsAny<CancellationToken>()))
            .Callback<Guid, RefreshToken, DateTime, CancellationToken>((_, s, _, _) => rotatedTo = s)
            .ReturnsAsync(true);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value!.AccessToken);
        Assert.Equal(Successor, result.Value.RefreshToken);
        Assert.Equal(AccessExpiry, result.Value.AccessTokenExpiresAtUtc);
        Assert.NotNull(rotatedTo);
        Assert.Equal(stored.FamilyId, rotatedTo.FamilyId);
        Assert.Equal(stored.FamilyStartedAtUtc, rotatedTo.FamilyStartedAtUtc);
        Assert.Equal("hash-of-" + Successor, rotatedTo.TokenHash);
        Assert.Equal(Now.AddDays(7), rotatedTo.ExpiresAtUtc);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_New_Access_Token_Is_Built_From_The_User_Read_At_Refresh_Time()
    {
        var user = CreateUser(UserRole.Admin);
        Arrange(StartFamily(user, Now.AddDays(-1)), user);

        await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        _users.Verify(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _tokenGenerator.Verify(t => t.GenerateAccessToken(user), Times.Once);
    }

    [Fact]
    public async Task A_Refresh_Does_Not_Record_A_Login()
    {
        var user = CreateUser(UserRole.Customer);
        Arrange(StartFamily(user, Now.AddDays(-1)), user);

        await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        Assert.Null(user.LastLoginAtUtc);
        _users.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(u => u.GetForUpdateByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Access_Token_Is_Generated_Before_The_Rotation()
    {
        var user = CreateUser(UserRole.Customer);
        Arrange(StartFamily(user, Now.AddDays(-1)), user);

        await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        Assert.Equal(["access", "rotate"], _calls);
    }

    [Fact]
    public async Task The_Expiry_Is_Capped_At_The_Family_Start_Plus_The_Maximum()
    {
        var user = CreateUser(UserRole.Customer);
        // A family started 26 days ago whose current token was issued 2 days ago: its sliding expiry
        // (+5 days) would pass the cap (+4 days), so the refresh is clamped to the cap.
        var stored = StartFamily(user, Now.AddDays(-26))
            .CreateSuccessor("hash-of-" + Presented, Now.AddDays(-2), Lifetime, Cap)!;
        Arrange(stored, user);
        RefreshToken? rotatedTo = null;
        _refreshTokens.Setup(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, RefreshToken, DateTime, CancellationToken>((_, s, _, _) => rotatedTo = s)
            .ReturnsAsync(true);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddDays(4), rotatedTo!.ExpiresAtUtc);
    }

    [Fact]
    public async Task An_Unknown_Token_Is_401_And_Nothing_Is_Revoked()
    {
        _refreshTokens.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((RefreshToken?)null);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        AssertUnauthorized(result);
        VerifyNothingRevokedOrRotated();
    }

    [Fact]
    public async Task An_Over_Long_Token_Is_401_Without_Hashing_Or_Reading()
    {
        var result = await CreateSubject().Handle(new RefreshTokenCommand(new string('a', 257)), CancellationToken.None);

        AssertUnauthorized(result);
        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
        _refreshTokens.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Token_Of_Exactly_256_Characters_Is_Looked_Up()
    {
        _refreshTokens.Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((RefreshToken?)null);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(new string('a', 256)), CancellationToken.None);

        AssertUnauthorized(result);
        _refreshTokens.Verify(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Expired_Token_Is_401_And_Does_Not_Revoke_The_Family()
    {
        var user = CreateUser(UserRole.Customer);
        var stored = StartFamily(user, Now.AddDays(-8));
        Arrange(stored, user);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        AssertUnauthorized(result);
        VerifyNothingRevokedOrRotated();
        _tokenGenerator.Verify(t => t.GenerateAccessToken(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task A_Revoked_Token_Revokes_The_Family_And_Issues_Nothing()
    {
        var user = CreateUser(UserRole.Customer);
        var stored = StartFamily(user, Now.AddDays(-1));
        Revoke(stored);
        Arrange(stored, user);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        AssertUnauthorized(result);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokens.Verify(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokenGenerator.Verify(t => t.GenerateAccessToken(It.IsAny<User>()), Times.Never);
        _tokenGenerator.Verify(t => t.GenerateRefreshToken(), Times.Never);
    }

    [Fact]
    public async Task A_Revoked_And_Expired_Token_Still_Revokes_The_Family()
    {
        var user = CreateUser(UserRole.Customer);
        var stored = StartFamily(user, Now.AddDays(-20));
        Revoke(stored);
        Arrange(stored, user);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        AssertUnauthorized(result);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Inactive_User_Is_401_And_The_Family_Is_Revoked()
    {
        var user = CreateUser(UserRole.Customer);
        user.Deactivate();
        var stored = StartFamily(user, Now.AddDays(-1));
        Arrange(stored, user);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        AssertUnauthorized(result);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokens.Verify(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_User_Who_Is_Gone_Is_401_And_The_Family_Is_Revoked()
    {
        var stored = StartFamily(CreateUser(UserRole.Customer), Now.AddDays(-1));
        Arrange(stored, null);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        AssertUnauthorized(result);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Family_At_Its_Cap_Is_401_Without_Revoking()
    {
        // The cap was lowered after the token was issued: the token is unexpired but the family is past it.
        var user = CreateUser(UserRole.Customer);
        var stored = StartFamily(user, Now.AddDays(-1));
        Arrange(stored, user);

        var result = await CreateSubject(familyMaxDays: 1).Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        AssertUnauthorized(result);
        VerifyNothingRevokedOrRotated();
        _tokenGenerator.Verify(t => t.GenerateAccessToken(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Losing_The_Rotation_Race_Revokes_The_Family_And_Is_401()
    {
        var user = CreateUser(UserRole.Customer);
        var stored = StartFamily(user, Now.AddDays(-1));
        Arrange(stored, user);
        _refreshTokens.Setup(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        AssertUnauthorized(result);
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(stored.FamilyId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_Invalid_Request_Is_400_With_No_Repository_Call(string? token)
    {
        var result = await CreateSubject().Handle(new RefreshTokenCommand(token), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        Assert.Equal("Invalid request: Refresh token is required.", result.Error.Details);
        _refreshTokens.VerifyNoOtherCalls();
        _users.VerifyNoOtherCalls();
        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Logs_Carry_The_Family_Id_But_Never_The_Token_Or_Its_Hash()
    {
        var user = CreateUser(UserRole.Customer);
        var stored = StartFamily(user, Now.AddDays(-1));
        Revoke(stored);
        Arrange(stored, user);

        await CreateSubject().Handle(new RefreshTokenCommand(Presented), CancellationToken.None);

        Assert.NotEmpty(_logger.Messages);
        Assert.Contains(_logger.Messages, m => m.Contains(stored.FamilyId.ToString()));
        Assert.All(_logger.Messages, m =>
        {
            Assert.DoesNotContain(Presented, m);
            Assert.DoesNotContain("hash-of-", m);
            Assert.DoesNotContain(user.Email.Value, m);
        });
    }

    private void Arrange(RefreshToken stored, User? user)
    {
        _refreshTokens.Setup(r => r.GetByHashAsync("hash-of-" + Presented, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        _users.Setup(u => u.GetByIdAsync(stored.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    private void VerifyNothingRevokedOrRotated()
    {
        _refreshTokens.Verify(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _refreshTokens.Verify(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertUnauthorized(SmartAppointments.BuildingBlocks.Models.Result<TokenResponse> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.Error!.Status);
        Assert.Equal(InvalidMessage, result.Error.Details);
    }

    private RefreshTokenCommandHandler CreateSubject(int familyMaxDays = 30) => new(
        _refreshTokens.Object,
        _users.Object,
        _tokenGenerator.Object,
        _hasher.Object,
        new RefreshTokenCommandValidator(),
        Options.Create(new JwtOptions { RefreshTokenExpirationDays = 7, RefreshTokenFamilyMaxDays = familyMaxDays }),
        new FixedTimeProvider(new DateTimeOffset(Now)),
        _logger);

    private static RefreshToken StartFamily(User user, DateTime issuedAtUtc) =>
        RefreshToken.StartFamily(user.Id, "hash-of-" + Presented, issuedAtUtc, Lifetime, Cap);

    // The entity has no Revoke method on purpose (revocation is a conditional statement in the repository).
    private static void Revoke(RefreshToken token) =>
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.RevokedAtUtc))!.SetValue(token, Now.AddHours(-1));

    private static User CreateUser(UserRole role) => role switch
    {
        UserRole.Admin => User.RegisterAdmin("Ada", "Admin", Email.Create("admin@example.com"), "+15551234567", "hash"),
        UserRole.Staff => User.RegisterStaff("Sam", "Staff", Email.Create("staff@example.com"), "+15551234567", "hash"),
        _ => User.RegisterCustomer("Cam", "Customer", Email.Create("customer@example.com"), "+15551234567", "hash")
    };

    private sealed class CapturingLogger : ILogger<RefreshTokenCommandHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
