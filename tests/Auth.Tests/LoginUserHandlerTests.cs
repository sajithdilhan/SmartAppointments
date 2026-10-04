using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Application.Models;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SmartAppointments.BuildingBlocks.Enums;

namespace Auth.Tests;

public class LoginUserHandlerTests
{
    private const string Password = "P@ssw0rd!";

    [Theory]
    [InlineData(UserRole.Customer)]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Admin)]
    public async Task Any_Active_Role_Can_Log_In(UserRole role)
    {
        // Regression guard: login previously rejected every non-Customer role, which left the
        // Admin- and Staff-only requirements with no reachable identity.
        var user = CreateUser(role);
        var repository = CreateRepository(user);

        var result = await CreateSubject(repository).Handle(new LoginUserCommand(user.Email.Value, Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value!.AccessToken);
        Assert.Equal(new DateTime(2026, 10, 4, 13, 0, 0, DateTimeKind.Utc), result.Value.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public async Task Successful_Login_Records_The_Login_Timestamp()
    {
        var user = CreateUser(UserRole.Customer);
        var repository = CreateRepository(user);

        await CreateSubject(repository).Handle(new LoginUserCommand(user.Email.Value, Password), CancellationToken.None);

        Assert.NotNull(user.LastLoginAtUtc);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Inactive_User_Is_Rejected()
    {
        var user = CreateUser(UserRole.Customer);
        user.Deactivate();
        var repository = CreateRepository(user);

        var result = await CreateSubject(repository).Handle(new LoginUserCommand(user.Email.Value, Password), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.Error!.Status);
    }

    [Fact]
    public async Task Unknown_Email_Returns_401_Not_404()
    {
        // A 404 here would let an unauthenticated caller enumerate registered addresses.
        var repository = CreateRepository(null);

        var result = await CreateSubject(repository).Handle(new LoginUserCommand("nobody@example.com", Password), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.Error!.Status);
    }

    [Fact]
    public async Task Wrong_Password_Is_Rejected()
    {
        var user = CreateUser(UserRole.Customer);
        var repository = CreateRepository(user);

        var result = await CreateSubject(repository).Handle(new LoginUserCommand(user.Email.Value, "WrongP@ss1"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.Error!.Status);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Successful_Login_Stages_One_Family_And_Saves_Once()
    {
        var user = CreateUser(UserRole.Customer);
        var repository = CreateRepository(user);
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        RefreshToken? staged = null;
        refreshTokens.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => staged = t)
            .Returns(Task.CompletedTask);

        var result = await CreateSubject(repository, refreshTokens).Handle(new LoginUserCommand(user.Email.Value, Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("refresh-token", result.Value!.RefreshToken);
        refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(staged);
        Assert.Equal(user.Id, staged.UserId);
        Assert.Equal("hash-of-refresh-token", staged.TokenHash);
        Assert.Equal(Now, staged.CreatedAtUtc);
        Assert.Equal(Now, staged.FamilyStartedAtUtc);
        Assert.Equal(Now.AddDays(7), staged.ExpiresAtUtc);
    }

    [Fact]
    public async Task A_Second_Login_Starts_A_Different_Family()
    {
        var user = CreateUser(UserRole.Customer);
        var repository = CreateRepository(user);
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        var staged = new List<RefreshToken>();
        refreshTokens.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => staged.Add(t))
            .Returns(Task.CompletedTask);
        var subject = CreateSubject(repository, refreshTokens);

        await subject.Handle(new LoginUserCommand(user.Email.Value, Password), CancellationToken.None);
        await subject.Handle(new LoginUserCommand(user.Email.Value, Password), CancellationToken.None);

        Assert.Equal(2, staged.Count);
        Assert.NotEqual(staged[0].FamilyId, staged[1].FamilyId);
        refreshTokens.Verify(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        refreshTokens.Verify(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Every_Rejection_Persists_No_Refresh_Token()
    {
        var inactive = CreateUser(UserRole.Customer);
        inactive.Deactivate();
        var active = CreateUser(UserRole.Customer);

        var cases = new (User? User, string Password)[] { (null, Password), (inactive, Password), (active, "WrongP@ss1") };
        foreach (var (user, password) in cases)
        {
            var refreshTokens = new Mock<IRefreshTokenRepository>();

            var result = await CreateSubject(CreateRepository(user), refreshTokens)
                .Handle(new LoginUserCommand("someone@example.com", password), CancellationToken.None);

            Assert.Equal(401, result.Error!.Status);
            refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-1, 30)]
    [InlineData(7, 0)]
    public async Task A_Misconfigured_Lifetime_Throws_And_Saves_Nothing(int days, int maxDays)
    {
        var user = CreateUser(UserRole.Customer);
        var repository = CreateRepository(user);
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        var subject = CreateSubject(repository, refreshTokens, days, maxDays);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => subject.Handle(new LoginUserCommand(user.Email.Value, Password), CancellationToken.None));

        refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(user.LastLoginAtUtc);
    }

    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private static LoginUserHandler CreateSubject(
        Mock<IUserRepository> repository,
        Mock<IRefreshTokenRepository>? refreshTokens = null,
        int refreshTokenExpirationDays = 7,
        int refreshTokenFamilyMaxDays = 30)
    {
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(h => h.Verify(Password, It.IsAny<string>())).Returns(true);

        var tokenGenerator = new Mock<ITokenGenerator>();
        tokenGenerator.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns(new AccessToken("access-token", new DateTime(2026, 10, 4, 13, 0, 0, DateTimeKind.Utc)));
        tokenGenerator.Setup(t => t.GenerateRefreshToken()).Returns("refresh-token");

        var hasher = new Mock<IRefreshTokenHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns((string t) => "hash-of-" + t);

        return new LoginUserHandler(
            repository.Object,
            passwordHasher.Object,
            NullLogger<LoginUserHandler>.Instance,
            tokenGenerator.Object,
            new LoginUserRequestValidator(),
            (refreshTokens ?? new Mock<IRefreshTokenRepository>()).Object,
            hasher.Object,
            Options.Create(new JwtOptions
            {
                RefreshTokenExpirationDays = refreshTokenExpirationDays,
                RefreshTokenFamilyMaxDays = refreshTokenFamilyMaxDays
            }),
            new FixedTimeProvider(new DateTimeOffset(Now)));
    }

    private static Mock<IUserRepository> CreateRepository(User? user)
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(r => r.GetForUpdateByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return repository;
    }

    private static User CreateUser(UserRole role) => role switch
    {
        UserRole.Admin => User.RegisterAdmin("Ada", "Admin", Email.Create("admin@example.com"), "+15551234567", "hash"),
        UserRole.Staff => User.RegisterStaff("Sam", "Staff", Email.Create("staff@example.com"), "+15551234567", "hash"),
        _ => User.RegisterCustomer("Cam", "Customer", Email.Create("customer@example.com"), "+15551234567", "hash")
    };
}
