using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Handlers;
using Availability.Application.Validations;
using Availability.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Availability.Tests;

public class ReserveSlotCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ISlotRepository> _slots = new();
    private readonly Mock<ISlotReservationRepository> _reservations = new();
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<IServiceTypeRepository> _serviceTypes = new();

    private readonly Branch _branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
    private readonly ServiceType _serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);
    private readonly Slot _slot;
    private readonly ReserveSlotCommand _command;

    public ReserveSlotCommandHandlerTests()
    {
        // Starts the day after "now".
        _slot = Slot.Create(
            _branch.Id, _serviceType.Id, new DateOnly(2030, 1, 7),
            new DateTime(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 4, 0, 0, DateTimeKind.Utc), 1);
        _command = new ReserveSlotCommand(_slot.Id, Guid.CreateVersion7());

        _slots.Setup(r => r.GetByIdAsync(_slot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_slot);
        _branches.Setup(r => r.GetByIdAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_branch);
        _serviceTypes.Setup(r => r.GetByIdAsync(_serviceType.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_serviceType);
        _reservations.Setup(r => r.ReserveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReserveOutcome.Reserved);
    }

    private ReserveSlotCommandHandler Handler(DateTimeOffset? now = null) => new(
        _slots.Object,
        _reservations.Object,
        _branches.Object,
        _serviceTypes.Object,
        new ReserveSlotCommandValidator(),
        new FixedTimeProvider(now ?? Now),
        NullLogger<ReserveSlotCommandHandler>.Instance);

    private void VerifyReserveNeverCalled() =>
        _reservations.Verify(r => r.ReserveAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Empty_Ids_Return_400()
    {
        Assert.Equal(400, (await Handler().Handle(_command with { SlotId = Guid.Empty }, CancellationToken.None)).Error!.Status);
        Assert.Equal(400, (await Handler().Handle(_command with { AppointmentId = Guid.Empty }, CancellationToken.None)).Error!.Status);
        VerifyReserveNeverCalled();
    }

    [Fact]
    public async Task A_Missing_Slot_Returns_404()
    {
        var command = _command with { SlotId = Guid.CreateVersion7() };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Slot '{command.SlotId}' was not found.", result.Error.Details);
        VerifyReserveNeverCalled();
    }

    [Fact]
    public async Task A_Free_Slot_Is_Reserved_At_The_Pinned_Time()
    {
        var result = await Handler().Handle(_command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _reservations.Verify(r => r.ReserveAsync(
            _slot.Id, _command.AppointmentId, Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Repeat_Reserve_Succeeds_Without_Consulting_State_Or_Reserving_Again()
    {
        _reservations.Setup(r => r.ExistsAsync(_slot.Id, _command.AppointmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // Everything that would fail a fresh reserve is true: the slot has started and both are closed.
        _branch.Deactivate();
        _serviceType.Deactivate();

        var result = await Handler(now: new DateTimeOffset(2030, 1, 8, 0, 0, 0, TimeSpan.Zero))
            .Handle(_command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        VerifyReserveNeverCalled();
        _branches.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _serviceTypes.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Slot_That_Has_Started_Returns_409()
    {
        var result = await Handler(now: new DateTimeOffset(_slot.StartUtc, TimeSpan.Zero)).Handle(_command, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Slot has already started.", result.Error.Details);
        VerifyReserveNeverCalled();
    }

    [Fact]
    public async Task A_Slot_One_Tick_Before_Its_Start_Can_Still_Be_Reserved()
    {
        var result = await Handler(now: new DateTimeOffset(_slot.StartUtc.AddTicks(-1), TimeSpan.Zero))
            .Handle(_command, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task An_Inactive_Branch_Returns_409()
    {
        _branch.Deactivate();

        var result = await Handler().Handle(_command, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Branch 'PG' is inactive.", result.Error.Details);
        VerifyReserveNeverCalled();
    }

    [Fact]
    public async Task An_Inactive_Service_Type_Returns_409()
    {
        _serviceType.Deactivate();

        var result = await Handler().Handle(_command, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Service type 'INSPECT' is inactive.", result.Error.Details);
        VerifyReserveNeverCalled();
    }

    [Fact]
    public async Task A_Full_Slot_Returns_409()
    {
        _reservations.Setup(r => r.ReserveAsync(
                _slot.Id, _command.AppointmentId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReserveOutcome.Full);

        var result = await Handler().Handle(_command, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Slot is full.", result.Error.Details);
    }

    [Fact]
    public async Task A_Duplicate_That_Slipped_Past_The_Early_Exit_Succeeds()
    {
        _reservations.Setup(r => r.ReserveAsync(
                _slot.Id, _command.AppointmentId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReserveOutcome.AlreadyReserved);

        var result = await Handler().Handle(_command, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
