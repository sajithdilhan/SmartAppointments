using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Handlers;
using Availability.Application.Validations;
using Availability.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Availability.Tests;

public class CreateBranchCommandHandlerTests
{
    private static readonly CreateBranchCommand ValidCommand =
        new(" pg ", " Pettah ", "Walk-ins welcome", "12 Main Street", "+94112345678");

    [Fact]
    public async Task A_Valid_Branch_Is_Staged_Then_Committed_And_Returned()
    {
        var repository = CreateRepository(codeExists: false);
        Branch? staged = null;
        repository.Setup(r => r.AddAsync(It.IsAny<Branch>(), It.IsAny<CancellationToken>()))
            .Callback<Branch, CancellationToken>((b, _) => staged = b);

        var result = await CreateSubject(repository).Handle(ValidCommand, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(staged);
        Assert.Equal(staged.Id, result.Value!.Id);
        Assert.Equal("PG", result.Value.Code);
        Assert.Equal("Pettah", result.Value.Name);
        Assert.True(result.Value.IsActive);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_Duplicate_Check_Receives_The_Normalised_Code()
    {
        // " pg " and "PG" are the same branch. Looking up the raw input would miss the duplicate
        // and leave it to the unique index, which is the slow path.
        var repository = CreateRepository(codeExists: false);

        await CreateSubject(repository).Handle(ValidCommand, CancellationToken.None);

        repository.Verify(r => r.ExistsByCodeAsync("PG", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Duplicate_Code_Returns_409_Without_Writing()
    {
        var repository = CreateRepository(codeExists: true);

        var result = await CreateSubject(repository).Handle(ValidCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("A branch with code 'PG' already exists.", result.Error.Details);
        repository.Verify(r => r.AddAsync(It.IsAny<Branch>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Duplicate_Detected_Only_At_Commit_Returns_409_Not_500()
    {
        // Two concurrent creates both pass the pre-check; the unique index rejects the loser at
        // commit. That must read as the same 409, not surface through ExceptionMiddleware as a 500.
        var repository = CreateRepository(codeExists: false);
        repository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateBranchCodeException("A branch with this code already exists."));

        var result = await CreateSubject(repository).Handle(ValidCommand, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("A branch with code 'PG' already exists.", result.Error.Details);
    }

    [Fact]
    public async Task An_Invalid_Request_Returns_400_Without_Touching_The_Repository()
    {
        var repository = CreateRepository(codeExists: false);
        var command = ValidCommand with { Code = "-", PhoneNumber = "nope" };

        var result = await CreateSubject(repository).Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        repository.Verify(r => r.ExistsByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateBranchCommandHandler CreateSubject(Mock<IBranchRepository> repository) =>
        new(repository.Object, new CreateBranchCommandValidator(), NullLogger<CreateBranchCommandHandler>.Instance);

    private static Mock<IBranchRepository> CreateRepository(bool codeExists)
    {
        var repository = new Mock<IBranchRepository>();
        repository.Setup(r => r.ExistsByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(codeExists);
        return repository;
    }
}
