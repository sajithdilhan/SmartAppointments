using Availability.Application.Abstractions;
using Availability.Application.Handlers;
using Availability.Application.Queries;
using Availability.Application.Validations;
using Availability.Domain.Entities;
using Moq;

namespace Availability.Tests;

public class SearchAvailableSlotsHandlerTests
{
    private static readonly DateOnly Monday = new(2030, 1, 7);
    private static readonly DateTimeOffset Now = new(2030, 1, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<IServiceTypeRepository> _serviceTypes = new();
    private readonly Mock<ISlotRepository> _slots = new();

    private readonly Branch _branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
    private readonly ServiceType _serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);

    public SearchAvailableSlotsHandlerTests()
    {
        _branch.SetSchedule("Asia/Colombo", [new WorkingHours(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))]);
        _branches.Setup(r => r.GetByIdAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_branch);
        _serviceTypes.Setup(r => r.GetByIdAsync(_serviceType.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_serviceType);
    }

    private SearchAvailableSlotsHandler Handler() => new(
        _branches.Object,
        _serviceTypes.Object,
        _slots.Object,
        new SearchAvailableSlotsQueryValidator(),
        new FixedTimeProvider(Now));

    private SearchAvailableSlotsQuery Query() => new(_branch.Id, _serviceType.Id, Monday);

    private void VerifySearchNeverCalled() =>
        _slots.Verify(r => r.SearchAvailableAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task A_Missing_Date_Returns_400()
    {
        var result = await Handler().Handle(Query() with { Date = null }, CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        VerifySearchNeverCalled();
    }

    [Fact]
    public async Task Empty_Ids_Return_400()
    {
        Assert.Equal(400, (await Handler().Handle(Query() with { BranchId = Guid.Empty }, CancellationToken.None)).Error!.Status);
        Assert.Equal(400, (await Handler().Handle(Query() with { ServiceTypeId = Guid.Empty }, CancellationToken.None)).Error!.Status);
    }

    [Fact]
    public async Task A_Missing_Branch_Returns_404()
    {
        var query = Query() with { BranchId = Guid.CreateVersion7() };

        var result = await Handler().Handle(query, CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Branch '{query.BranchId}' was not found.", result.Error.Details);
        VerifySearchNeverCalled();
    }

    [Fact]
    public async Task An_Inactive_Branch_Returns_404()
    {
        _branch.Deactivate();

        var result = await Handler().Handle(Query(), CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        VerifySearchNeverCalled();
    }

    [Fact]
    public async Task A_Missing_Service_Type_Returns_404()
    {
        var query = Query() with { ServiceTypeId = Guid.CreateVersion7() };

        var result = await Handler().Handle(query, CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Service type '{query.ServiceTypeId}' was not found.", result.Error.Details);
        VerifySearchNeverCalled();
    }

    [Fact]
    public async Task An_Inactive_Service_Type_Returns_404()
    {
        _serviceType.Deactivate();

        var result = await Handler().Handle(Query(), CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        VerifySearchNeverCalled();
    }

    [Fact]
    public async Task Local_Times_Are_Computed_In_The_Branch_Zone()
    {
        var slot = Slot.Create(
            _branch.Id, _serviceType.Id, Monday,
            new DateTime(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 4, 0, 0, DateTimeKind.Utc), 5);
        _slots.Setup(r => r.SearchAvailableAsync(
                _branch.Id, _serviceType.Id, Monday, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([slot]);

        var result = await Handler().Handle(Query(), CancellationToken.None);

        var response = Assert.Single(result.Value!);
        Assert.Equal(slot.Id, response.Id);
        Assert.Equal(Monday, response.Date);
        Assert.Equal(new TimeOnly(9, 0), response.LocalStartTime);
        Assert.Equal(new TimeOnly(9, 30), response.LocalEndTime);
        Assert.Equal(slot.StartUtc, response.StartAtUtc);
        Assert.Equal(slot.EndUtc, response.EndAtUtc);
        Assert.Equal(5, response.Capacity);
        Assert.Equal(5, response.AvailableCapacity);
    }

    [Fact]
    public async Task The_Repository_Is_Asked_For_Slots_After_The_Pinned_Now()
    {
        _slots.Setup(r => r.SearchAvailableAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Handler().Handle(Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        _slots.Verify(r => r.SearchAvailableAsync(
            _branch.Id, _serviceType.Id, Monday, Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Branch_Without_A_Schedule_Returns_An_Empty_List_Without_Searching()
    {
        var unscheduled = Branch.Create("KY", "Kandy", null, "1 Hill Street", "+94112345678");
        _branches.Setup(r => r.GetByIdAsync(unscheduled.Id, It.IsAny<CancellationToken>())).ReturnsAsync(unscheduled);

        var result = await Handler().Handle(Query() with { BranchId = unscheduled.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        VerifySearchNeverCalled();
    }
}
