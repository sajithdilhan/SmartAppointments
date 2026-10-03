using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Handlers;
using Availability.Application.Validations;
using Availability.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Availability.Tests;

public class ReleaseSlotCommandHandlerTests
{
    private readonly Mock<ISlotRepository> _slots = new();
    private readonly Mock<ISlotReservationRepository> _reservations = new();

    private readonly Slot _slot = Slot.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2030, 1, 7),
        new DateTime(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 4, 0, 0, DateTimeKind.Utc), 1);

    private readonly ReleaseSlotCommand _command;

    public ReleaseSlotCommandHandlerTests()
    {
        _command = new ReleaseSlotCommand(_slot.Id, Guid.CreateVersion7());
        _slots.Setup(r => r.GetByIdAsync(_slot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_slot);
    }

    private ReleaseSlotCommandHandler Handler() => new(
        _slots.Object,
        _reservations.Object,
        new ReleaseSlotCommandValidator(),
        NullLogger<ReleaseSlotCommandHandler>.Instance);

    private void VerifyReleaseNeverCalled() =>
        _reservations.Verify(r => r.ReleaseAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Empty_Ids_Return_400()
    {
        Assert.Equal(400, (await Handler().Handle(_command with { SlotId = Guid.Empty }, CancellationToken.None)).Error!.Status);
        Assert.Equal(400, (await Handler().Handle(_command with { AppointmentId = Guid.Empty }, CancellationToken.None)).Error!.Status);
        VerifyReleaseNeverCalled();
    }

    [Fact]
    public async Task A_Missing_Slot_Returns_404()
    {
        var command = _command with { SlotId = Guid.CreateVersion7() };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Slot '{command.SlotId}' was not found.", result.Error.Details);
        VerifyReleaseNeverCalled();
    }

    [Theory]
    [InlineData(ReleaseOutcome.Released)]
    [InlineData(ReleaseOutcome.NotReserved)]
    public async Task Either_Outcome_Of_The_Repository_Is_A_Success(ReleaseOutcome outcome)
    {
        _reservations.Setup(r => r.ReleaseAsync(_slot.Id, _command.AppointmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

        var result = await Handler().Handle(_command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _reservations.Verify(r => r.ReleaseAsync(_slot.Id, _command.AppointmentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Release_Makes_No_State_Checks()
    {
        // The handler is given no branch or service type repository and no clock; the slot lookup
        // is its only read, so a started slot or a closed branch cannot block a release.
        var result = await Handler().Handle(_command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _reservations.Verify(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
