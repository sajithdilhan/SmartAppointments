using Availability.Application.Abstractions;
using Availability.Application.Handlers;
using Availability.Application.Queries;
using Availability.Domain.Entities;
using Moq;

namespace Availability.Tests;

public class GetInternalBranchHandlerTests
{
    private readonly Mock<IBranchRepository> _branches = new();

    private static Branch NewBranch() => Branch.Create("DT01", "Downtown", null, "1 Main St", "+15550100");

    [Fact]
    public async Task A_Missing_Branch_Returns_404()
    {
        var id = Guid.CreateVersion7();

        var result = await new GetInternalBranchHandler(_branches.Object).Handle(new GetInternalBranchQuery(id), CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
    }

    [Fact]
    public async Task An_Active_Branch_Without_A_Schedule_Has_A_Null_Time_Zone()
    {
        var branch = NewBranch();
        _branches.Setup(r => r.GetByIdAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);

        var result = await new GetInternalBranchHandler(_branches.Object).Handle(new GetInternalBranchQuery(branch.Id), CancellationToken.None);

        Assert.Equal(branch.Id, result.Value!.Id);
        Assert.True(result.Value.IsActive);
        Assert.Null(result.Value.TimeZoneId);
    }

    [Fact]
    public async Task An_Inactive_Branch_Is_Returned_With_Its_Time_Zone()
    {
        var branch = NewBranch();
        branch.SetSchedule("Europe/London", []);
        branch.Deactivate();
        _branches.Setup(r => r.GetByIdAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);

        var result = await new GetInternalBranchHandler(_branches.Object).Handle(new GetInternalBranchQuery(branch.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
        Assert.Equal("Europe/London", result.Value.TimeZoneId);
    }
}
