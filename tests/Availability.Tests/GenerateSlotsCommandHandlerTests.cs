using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Handlers;
using Availability.Application.Validations;
using Availability.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Availability.Tests;

public class GenerateSlotsCommandHandlerTests
{
    // 7 January 2030 is a Monday. The branch opens 09:00-11:00 and the service lasts 30 minutes,
    // so there are four candidates, the first at 03:30Z (09:00 in Colombo, +05:30).
    private static readonly DateOnly Monday = new(2030, 1, 7);
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<IServiceTypeRepository> _serviceTypes = new();
    private readonly Mock<ISlotRepository> _slots = new();

    private readonly Branch _branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
    private readonly ServiceType _serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);

    public GenerateSlotsCommandHandlerTests()
    {
        _branch.SetSchedule("Asia/Colombo", [new WorkingHours(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0))]);
        _branches.Setup(r => r.GetByIdAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_branch);
        _serviceTypes.Setup(r => r.GetByIdAsync(_serviceType.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_serviceType);
        _slots.Setup(r => r.ListOverlappingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private GenerateSlotsCommandHandler Handler(DateTimeOffset? now = null) => new(
        _branches.Object,
        _serviceTypes.Object,
        _slots.Object,
        new GenerateSlotsCommandValidator(),
        new FixedTimeProvider(now ?? Now),
        NullLogger<GenerateSlotsCommandHandler>.Instance);

    private GenerateSlotsCommand Command() => new(_branch.Id, _serviceType.Id, Monday, Monday, 5);

    private void VerifyNothingWritten()
    {
        _slots.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Slot>>(), It.IsAny<CancellationToken>()), Times.Never);
        _slots.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invalid_Input_Returns_400_Without_Loading_Anything()
    {
        var result = await Handler().Handle(Command() with { Capacity = 0 }, CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        Assert.StartsWith("Invalid request data. Errors: ", result.Error.Details);
        _branches.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task A_Missing_Branch_Returns_404_And_Writes_Nothing()
    {
        var command = Command() with { BranchId = Guid.CreateVersion7() };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Branch '{command.BranchId}' was not found.", result.Error.Details);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task A_Missing_Service_Type_Returns_404_And_Writes_Nothing()
    {
        var command = Command() with { ServiceTypeId = Guid.CreateVersion7() };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Service type '{command.ServiceTypeId}' was not found.", result.Error.Details);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task An_Inactive_Branch_Returns_409_And_Writes_Nothing()
    {
        _branch.Deactivate();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Branch 'PG' is inactive.", result.Error.Details);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task An_Inactive_Service_Type_Returns_409_And_Writes_Nothing()
    {
        _serviceType.Deactivate();

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Service type 'INSPECT' is inactive.", result.Error.Details);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task A_Branch_Without_A_Schedule_Returns_409_And_Writes_Nothing()
    {
        var unscheduled = Branch.Create("KY", "Kandy", null, "1 Hill Street", "+94112345678");
        _branches.Setup(r => r.GetByIdAsync(unscheduled.Id, It.IsAny<CancellationToken>())).ReturnsAsync(unscheduled);

        var result = await Handler().Handle(Command() with { BranchId = unscheduled.Id }, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Branch 'KY' has no working hours.", result.Error.Details);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task A_Branch_With_An_Empty_Schedule_Returns_409_And_Writes_Nothing()
    {
        _branch.SetSchedule("Asia/Colombo", []);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Branch 'PG' has no working hours.", result.Error.Details);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Generating_Creates_Every_Candidate_And_Commits_Once()
    {
        List<Slot>? staged = null;
        _slots.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Slot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Slot>, CancellationToken>((s, _) => staged = s.ToList());

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Equal(4, staged!.Count);
        Assert.Equal(new DateTime(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc), staged[0].StartUtc);
        Assert.All(staged, s => Assert.Equal(5, s.Capacity));
        _slots.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Candidate_Overlapping_An_Existing_Slot_Is_Skipped_And_Counted()
    {
        // 03:45Z-04:15Z straddles the first two candidates (03:30-04:00 and 04:00-04:30).
        var existing = Slot.Create(
            _branch.Id, _serviceType.Id, Monday,
            new DateTime(2030, 1, 7, 3, 45, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 4, 15, 0, DateTimeKind.Utc), 5);
        _slots.Setup(r => r.ListOverlappingAsync(
                _branch.Id, _serviceType.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);
        List<Slot>? staged = null;
        _slots.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Slot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Slot>, CancellationToken>((s, _) => staged = s.ToList());

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(2, result.Value.SkippedCount);
        Assert.Equal(
            [new DateTime(2030, 1, 7, 4, 30, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 5, 0, 0, DateTimeKind.Utc)],
            staged!.Select(s => s.StartUtc));
    }

    [Fact]
    public async Task A_Slot_That_Merely_Touches_A_Candidate_Does_Not_Overlap_It()
    {
        // Ends exactly when the first candidate starts.
        var existing = Slot.Create(
            _branch.Id, _serviceType.Id, Monday,
            new DateTime(2030, 1, 7, 3, 0, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc), 5);
        _slots.Setup(r => r.ListOverlappingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(4, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
    }

    [Fact]
    public async Task A_Second_Run_Where_Everything_Overlaps_Creates_Nothing_And_Does_Not_Save()
    {
        var existing = SlotPlanner.Plan(_branch, _serviceType, Monday, Monday, 5, Now.UtcDateTime);
        _slots.Setup(r => r.ListOverlappingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing.ToList());

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(4, result.Value.SkippedCount);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task No_Candidates_Returns_Zero_Without_Querying_Or_Writing()
    {
        // The pinned clock is after the whole day, so every candidate is in the past.
        var result = await Handler(new DateTimeOffset(2030, 1, 8, 0, 0, 0, TimeSpan.Zero))
            .Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        _slots.Verify(r => r.ListOverlappingAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task The_Pinned_Clock_Decides_Which_Candidates_Are_In_The_Past()
    {
        // 04:10Z is after the first two candidates have started (03:30Z, 04:00Z).
        var result = await Handler(new DateTimeOffset(2030, 1, 7, 4, 10, 0, TimeSpan.Zero))
            .Handle(Command(), CancellationToken.None);

        Assert.Equal(2, result.Value!.CreatedCount);
    }

    [Fact]
    public async Task Losing_A_Race_At_Commit_Returns_409_Not_500()
    {
        _slots.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateSlotException("duplicate"));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal(
            "Slots for this branch and service type were generated by a concurrent request. Try again.",
            result.Error.Details);
    }
}
