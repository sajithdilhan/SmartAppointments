using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static LoginUserHandler CreateSubject(Mock<IUserRepository> repository)
    {
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(h => h.Verify(Password, It.IsAny<string>())).Returns(true);

        var tokenGenerator = new Mock<ITokenGenerator>();
        tokenGenerator.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access-token");
        tokenGenerator.Setup(t => t.GenerateRefreshToken()).Returns("refresh-token");

        return new LoginUserHandler(
            repository.Object,
            passwordHasher.Object,
            NullLogger<LoginUserHandler>.Instance,
            tokenGenerator.Object,
            new LoginUserRequestValidator());
    }

    private static Mock<IUserRepository> CreateRepository(User? user)
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
