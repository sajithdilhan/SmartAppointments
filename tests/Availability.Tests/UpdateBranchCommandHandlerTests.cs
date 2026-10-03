using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Handlers;
using Availability.Application.Validations;
using Availability.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Availability.Tests;

public class UpdateBranchCommandHandlerTests
{
    [Fact]
    public async Task A_Valid_Update_Replaces_The_Details_And_Commits()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        var repository = CreateRepository(branch);

        var result = await CreateSubject(repository).Handle(
            new UpdateBranchCommand(branch.Id, " Pettah North ", "Walk-ins welcome", "14 Main Street", "+94119999999"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Pettah North", result.Value!.Name);
        Assert.Equal("Walk-ins welcome", result.Value.Description);
        Assert.Equal("14 Main Street", result.Value.Address);
        Assert.Equal("+94119999999", result.Value.PhoneNumber);
        Assert.NotNull(result.Value.UpdatedAtUtc);
        Assert.Equal("PG", result.Value.Code);
        Assert.True(result.Value.IsActive);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Inactive_Branch_Can_Still_Be_Updated_And_Stays_Inactive()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        branch.Deactivate();
        var repository = CreateRepository(branch);

        var result = await CreateSubject(repository).Handle(
            new UpdateBranchCommand(branch.Id, "Pettah North", null, "14 Main Street", "+94119999999"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
    }

    [Fact]
    public async Task An_Unknown_Branch_Returns_404_Without_Writing()
    {
        var repository = CreateRepository(branch: null);
        var id = Guid.CreateVersion7();

        var result = await CreateSubject(repository).Handle(
            new UpdateBranchCommand(id, "Pettah", null, "12 Main Street", "+94112345678"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Branch '{id}' was not found.", result.Error.Details);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Invalid_Request_Returns_400_Without_Touching_The_Repository()
    {
        var repository = CreateRepository(branch: null);

        var result = await CreateSubject(repository).Handle(
            new UpdateBranchCommand(Guid.CreateVersion7(), "", null, "12 Main Street", "nope"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        repository.Verify(r => r.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static UpdateBranchCommandHandler CreateSubject(Mock<IBranchRepository> repository) =>
        new(repository.Object, new UpdateBranchCommandValidator(), NullLogger<UpdateBranchCommandHandler>.Instance);

    private static Mock<IBranchRepository> CreateRepository(Branch? branch)
    {
        var repository = new Mock<IBranchRepository>();
        repository.Setup(r => r.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        return repository;
    }
}
