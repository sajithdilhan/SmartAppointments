using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Handlers;
using Availability.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Availability.Tests;

public class SetBranchActiveCommandHandlerTests
{
    [Fact]
    public async Task Deactivating_An_Active_Branch_Flips_It_And_Commits()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        var repository = CreateRepository(branch);

        var result = await CreateSubject(repository).Handle(new SetBranchActiveCommand(branch.Id, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(branch.IsActive);
        Assert.NotNull(branch.UpdatedAtUtc);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Activating_An_Inactive_Branch_Flips_It_And_Commits()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        branch.Deactivate();
        var repository = CreateRepository(branch);

        var result = await CreateSubject(repository).Handle(new SetBranchActiveCommand(branch.Id, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(branch.IsActive);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Requesting_The_Current_State_Succeeds_Without_Writing(bool isActive)
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        if (!isActive)
        {
            branch.Deactivate();
        }
        var repository = CreateRepository(branch);

        var result = await CreateSubject(repository).Handle(new SetBranchActiveCommand(branch.Id, isActive), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(isActive, branch.IsActive);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unknown_Branch_Returns_404()
    {
        var repository = CreateRepository(branch: null);

        var result = await CreateSubject(repository).Handle(new SetBranchActiveCommand(Guid.CreateVersion7(), false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.Status);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SetBranchActiveCommandHandler CreateSubject(Mock<IBranchRepository> repository) =>
        new(repository.Object, NullLogger<SetBranchActiveCommandHandler>.Instance);

    private static Mock<IBranchRepository> CreateRepository(Branch? branch)
    {
        var repository = new Mock<IBranchRepository>();
        repository.Setup(r => r.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        return repository;
    }
}
