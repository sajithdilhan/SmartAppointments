using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auth.Tests;

public class RegisterCustomerCommandHandlerTests
{
    private static readonly RegisterCustomerCommand ValidCommand =
        new("Cam", "Customer", "cam@example.com", "+15551234567", "P@ssw0rd!23");

    [Fact]
    public async Task Registration_Stages_Then_Commits_Explicitly()
    {
        // The repository no longer commits inside AddAsync, so the handler owns the commit.
        // Losing the explicit SaveChangesAsync would silently drop every registration.
        var repository = CreateRepository(existing: null);

        var result = await CreateSubject(repository).Handle(ValidCommand, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Duplicate_Email_Is_Rejected_Without_Writing()
    {
        var existing = User.RegisterCustomer("Cam", "Customer", Email.Create("cam@example.com"), "+15551234567", "hash");
        var repository = CreateRepository(existing);

        var result = await CreateSubject(repository).Handle(ValidCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        repository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Duplicate_Check_Uses_The_Read_Only_Query()
    {
        // The existence check never mutates, so it must not take a tracked entity.
        var repository = CreateRepository(existing: null);

        await CreateSubject(repository).Handle(ValidCommand, CancellationToken.None);

        repository.Verify(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.GetForUpdateByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invalid_Request_Is_Rejected_Without_Writing()
    {
        var repository = CreateRepository(existing: null);
        var command = ValidCommand with { Email = "not-an-email", Password = "weak" };

        var result = await CreateSubject(repository).Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Duplicate_Detected_Only_At_Commit_Returns_400_Not_500()
    {
        // Two concurrent registrations both pass the read-then-write check; the unique index
        // rejects the loser at commit. That must read as the same 400 as the pre-check, not as
        // an unhandled exception surfacing through ExceptionMiddleware as a 500.
        var repository = CreateRepository(existing: null);
        repository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateEmailException("Email is already registered."));

        var result = await CreateSubject(repository).Handle(ValidCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        Assert.Equal("Email is already registered.", result.Error.Details);
    }

    private static RegisterCustomerCommandHandler CreateSubject(Mock<IUserRepository> repository)
    {
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");

        return new RegisterCustomerCommandHandler(
            repository.Object,
            passwordHasher.Object,
            new RegisterCustomerCommandValidator(),
            NullLogger<RegisterCustomerCommandHandler>.Instance);
    }

    private static Mock<IUserRepository> CreateRepository(User? existing)
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        return repository;
    }
}
