using Availability.Application.Abstractions;
using Availability.Application.Handlers;
using Availability.Application.Queries;
using Availability.Domain.Entities;
using Moq;
using SmartAppointments.BuildingBlocks;

namespace Availability.Tests;

public class GetBranchesHandlerTests
{
    [Theory]
    [InlineData(Constants.AdminRole, true, true)]
    [InlineData(Constants.AdminRole, false, false)]
    [InlineData(Constants.StaffRole, true, false)]
    [InlineData(Constants.CustomerRole, true, false)]
    [InlineData(null, true, false)]
    public async Task Only_An_Admin_Who_Asks_Sees_Inactive_Branches(string? role, bool includeInactive, bool expected)
    {
        // A non-admin asking for inactive branches is not rejected; the flag is just ignored.
        var repository = new Mock<IBranchRepository>();
        repository.Setup(r => r.ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await new GetBranchesHandler(repository.Object)
            .Handle(new GetBranchesQuery(includeInactive, role), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Verify(r => r.ListAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Branches_Are_Returned_In_The_Order_The_Repository_Gives_Them()
    {
        // Ordering by name then code is the repository's job; the handler must not undo it.
        var zeta = Branch.Create("ZT", "Zeta", null, "1 Road", "+94112345678");
        var alpha = Branch.Create("AL", "Alpha", null, "2 Road", "+94112345678");
        var repository = new Mock<IBranchRepository>();
        repository.Setup(r => r.ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync([alpha, zeta]);

        var result = await new GetBranchesHandler(repository.Object)
            .Handle(new GetBranchesQuery(false, Constants.CustomerRole), CancellationToken.None);

        Assert.Equal([alpha.Id, zeta.Id], result.Value!.Select(b => b.Id));
    }
}
