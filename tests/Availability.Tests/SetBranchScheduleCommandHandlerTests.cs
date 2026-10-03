using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Handlers;
using Availability.Application.Models;
using Availability.Application.Validations;
using Availability.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Availability.Tests;

public class SetBranchScheduleCommandHandlerTests
{
    private readonly Mock<IBranchRepository> _repository = new();

    private SetBranchScheduleCommandHandler Handler() =>
        new(_repository.Object, new SetBranchScheduleCommandValidator(), NullLogger<SetBranchScheduleCommandHandler>.Instance);

    [Fact]
    public async Task A_Missing_Branch_Returns_404_Without_Saving()
    {
        var id = Guid.CreateVersion7();
        _repository.Setup(r => r.GetForUpdateByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Branch?)null);

        var result = await Handler().Handle(
            new SetBranchScheduleCommand(id, "Asia/Colombo", []), CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Branch '{id}' was not found.", result.Error.Details);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invalid_Input_Returns_400_Without_Loading_The_Branch()
    {
        var result = await Handler().Handle(
            new SetBranchScheduleCommand(Guid.CreateVersion7(), "Sri Lanka Standard Time", []), CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        Assert.StartsWith("Invalid request data. Errors: ", result.Error.Details);
        _repository.Verify(r => r.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Success_Saves_Once_And_Returns_The_Zone_And_Monday_First_Hours()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        _repository.Setup(r => r.GetForUpdateByIdAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);

        var result = await Handler().Handle(
            new SetBranchScheduleCommand(
                branch.Id,
                "Asia/Colombo",
                [
                    new WorkingHoursRequest(DayOfWeek.Sunday, "10:00", "14:00"),
                    new WorkingHoursRequest(DayOfWeek.Wednesday, "08:30", "16:45"),
                    new WorkingHoursRequest(DayOfWeek.Monday, "09:00", "17:00")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Asia/Colombo", result.Value!.TimeZoneId);
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Sunday],
            result.Value.WorkingHours.Select(w => w.DayOfWeek));
        Assert.Equal(new TimeOnly(8, 30), result.Value.WorkingHours[1].OpensAt);
        Assert.Equal(new TimeOnly(16, 45), result.Value.WorkingHours[1].ClosesAt);
        Assert.NotNull(result.Value.UpdatedAtUtc);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
