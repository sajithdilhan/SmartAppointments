using Availability.Application.Abstractions;
using Availability.Application.Handlers;
using Availability.Application.Queries;
using Availability.Domain.Entities;
using Moq;
using SmartAppointments.BuildingBlocks;

namespace Availability.Tests;

public class GetBranchHandlerTests
{
    [Theory]
    [InlineData(Constants.AdminRole)]
    [InlineData(Constants.StaffRole)]
    [InlineData(Constants.CustomerRole)]
    public async Task An_Active_Branch_Is_Returned_To_Every_Role(string role)
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");

        var result = await CreateSubject(branch).Handle(new GetBranchQuery(branch.Id, role), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(branch.Id, result.Value!.Id);
    }

    [Fact]
    public async Task An_Inactive_Branch_Is_Returned_To_An_Admin()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        branch.Deactivate();

        var result = await CreateSubject(branch).Handle(new GetBranchQuery(branch.Id, Constants.AdminRole), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
    }

    [Theory]
    [InlineData(Constants.StaffRole)]
    [InlineData(Constants.CustomerRole)]
    public async Task An_Inactive_Branch_Looks_Exactly_Like_A_Missing_One_To_A_Non_Admin(string role)
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        branch.Deactivate();

        var inactive = await CreateSubject(branch).Handle(new GetBranchQuery(branch.Id, role), CancellationToken.None);
        var missing = await CreateSubject(null).Handle(new GetBranchQuery(branch.Id, role), CancellationToken.None);

        Assert.False(inactive.IsSuccess);
        Assert.Equal(404, inactive.Error!.Status);
        Assert.Equal(missing.Error, inactive.Error);
    }

    [Fact]
    public async Task The_Lookup_Is_Read_Only()
    {
        var repository = new Mock<IBranchRepository>();

        await new GetBranchHandler(repository.Object)
            .Handle(new GetBranchQuery(Guid.CreateVersion7(), Constants.AdminRole), CancellationToken.None);

        repository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static GetBranchHandler CreateSubject(Branch? branch)
    {
        var repository = new Mock<IBranchRepository>();
        repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        return new GetBranchHandler(repository.Object);
    }
}
