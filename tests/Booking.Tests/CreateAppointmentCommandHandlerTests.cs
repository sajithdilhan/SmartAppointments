using System.Text.Json;
using Booking.Application.Abstractions;
using Booking.Application.Commands;
using Booking.Application.Handlers;
using Booking.Application.Models;
using Booking.Application.Validations;
using Booking.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Tests;

public class CreateAppointmentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly Mock<IIdempotencyRepository> _idempotency = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IAvailabilityClient> _availability = new();

    private readonly CreateAppointmentCommand _command = new(Guid.CreateVersion7(), "key-1", Guid.CreateVersion7());
    private readonly SlotInfo _slot;
    private readonly IdempotencyRecord _record;
    private readonly List<string> _calls = [];

    public CreateAppointmentCommandHandlerTests()
    {
        _slot = new SlotInfo(
            _command.SlotId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new DateTime(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 4, 0, 0, DateTimeKind.Utc), 1, 0, 1);
        _record = IdempotencyRecord.Claim(_command.CustomerId, "key-1", "hash", Guid.CreateVersion7(), Now.UtcDateTime);

        _idempotency.Setup(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("claim"))
            .ReturnsAsync(new ClaimResult.Claimed(_record));
        _availability.Setup(a => a.GetSlotAsync(_slot.Id, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("get"))
            .ReturnsAsync(Result<SlotInfo>.Success(_slot));
        _appointments.Setup(r => r.HasOverlappingBookedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("overlap"))
            .ReturnsAsync(false);
        _availability.Setup(a => a.ReserveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("reserve"))
            .ReturnsAsync(Result<bool>.Success(true));
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("release"))
            .ReturnsAsync(Result<bool>.Success(true));
        _appointments.Setup(r => r.TryAddBookedAsync(It.IsAny<Appointment>(), It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("save"))
            .ReturnsAsync(AddAppointmentOutcome.Saved);
    }

    private CreateAppointmentCommandHandler CreateHandler() => new(
        _idempotency.Object, _appointments.Object, _availability.Object,
        new CreateAppointmentCommandValidator(), new FixedTimeProvider(Now),
        NullLogger<CreateAppointmentCommandHandler>.Instance);

    private Task<Result<AppointmentResponse>> Send(CreateAppointmentCommand? command = null) =>
        CreateHandler().Handle(command ?? _command, CancellationToken.None);

    private void VerifyStored(int status, Times? times = null) =>
        _idempotency.Verify(r => r.CompleteAsync(_record, status, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            times ?? Times.Once());

    private void VerifyNothingStored() =>
        _idempotency.Verify(r => r.CompleteAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

    private void VerifyClaimRemoved(Times times) =>
        _idempotency.Verify(r => r.RemoveAsync(_record, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Happy_Path_Calls_In_Order_Saves_The_Appointment_And_Returns_It()
    {
        var result = await Send();

        Assert.True(result.IsSuccess);
        Assert.Equal(["claim", "get", "overlap", "reserve", "save"], _calls);
        var response = result.Value!;
        Assert.Equal(_record.AppointmentId, response.Id);
        Assert.Equal(_command.CustomerId, response.CustomerId);
        Assert.Equal(_slot.StartUtc, response.StartUtc);
        Assert.Equal(AppointmentStatus.Booked, response.Status);
        _availability.Verify(a => a.ReserveAsync(_slot.Id, _record.AppointmentId, It.IsAny<CancellationToken>()), Times.Once);
        // The outcome is stored by the save itself: the record handed over is completed with the 201 body.
        _appointments.Verify(r => r.TryAddBookedAsync(
            It.Is<Appointment>(a => a.Id == response.Id),
            It.Is<IdempotencyRecord>(i => i == _record && i.State == IdempotencyState.Completed && i.StatusCode == 201
                && JsonSerializer.Deserialize<AppointmentResponse>(i.ResponseBody!, Json) == response),
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyNothingStored();
    }

    [Fact]
    public async Task The_Request_Hash_Is_The_Sha256_Of_The_Slot_Id()
    {
        string? hash = null;
        _idempotency.Setup(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, Guid, DateTime, CancellationToken>((_, _, h, _, _, _) => hash = h)
            .ReturnsAsync(new ClaimResult.Claimed(_record));

        await Send();
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(_command.SlotId.ToString("D").ToLowerInvariant()))).ToLowerInvariant();

        Assert.Equal(expected, hash);
        Assert.Equal(64, hash!.Length);
    }

    [Fact]
    public async Task An_Invalid_Command_Is_400_And_Nothing_Is_Claimed()
    {
        var result = await Send(_command with { IdempotencyKey = null });

        Assert.Equal(400, result.Error!.Status);
        _idempotency.Verify(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Replayed_Success_Is_Returned_Without_Calling_Availability()
    {
        var stored = new AppointmentResponse(
            Guid.CreateVersion7(), _command.CustomerId, _slot.Id, _slot.BranchId, _slot.ServiceTypeId,
            _slot.StartUtc, _slot.EndUtc, AppointmentStatus.Booked, Now.UtcDateTime, Now.UtcDateTime, null);
        _idempotency.Setup(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimResult.Replay(201, JsonSerializer.Serialize(stored, Json)));

        var result = await Send();

        Assert.True(result.IsSuccess);
        Assert.Equal(stored, result.Value);
        _availability.VerifyNoOtherCalls();
        _appointments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Replayed_Failure_Is_Returned_With_Its_Stored_Status_And_Message()
    {
        _idempotency.Setup(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimResult.Replay(409, JsonSerializer.Serialize(new Error(409, "Slot is full."), Json)));

        var result = await Send();

        Assert.Equal(new Error(409, "Slot is full."), result.Error);
        _availability.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Failure_Stored_Before_The_Error_Body_Change_Still_Replays()
    {
        // The stored format is the internal Error record, not the HTTP body, so old rows need no migration.
        _idempotency.Setup(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimResult.Replay(409, "{\"status\":409,\"details\":\"Slot is full.\"}"));

        var result = await Send();

        Assert.Equal(new Error(409, "Slot is full."), result.Error);
    }

    [Fact]
    public async Task A_Hash_Mismatch_Is_422()
    {
        _idempotency.Setup(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimResult.HashMismatch());

        var result = await Send();

        Assert.Equal(422, result.Error!.Status);
        _availability.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Key_In_Progress_Is_409_And_Nothing_Else_Happens()
    {
        _idempotency.Setup(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimResult.InProgress());

        var result = await Send();

        Assert.Equal(409, result.Error!.Status);
        _availability.VerifyNoOtherCalls();
        VerifyNothingStored();
    }

    [Fact]
    public async Task An_Unknown_Slot_Is_404_And_Stored()
    {
        _availability.Setup(a => a.GetSlotAsync(_slot.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SlotInfo>.Failure(new Error(404, "Slot not found.")));

        var result = await Send();

        Assert.Equal(404, result.Error!.Status);
        VerifyStored(404);
        _availability.Verify(a => a.ReserveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Overlap_Is_409_Stored_And_Nothing_Is_Reserved()
    {
        _appointments.Setup(r => r.HasOverlappingBookedAsync(_command.CustomerId, _slot.StartUtc, _slot.EndUtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Send();

        Assert.Equal(409, result.Error!.Status);
        VerifyStored(409);
        _availability.Verify(a => a.ReserveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Availabilitys_409_On_Reserve_Is_Forwarded_And_Stored_Not_Treated_As_An_Attempt()
    {
        _availability.Setup(a => a.ReserveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(409, "Slot is full.")));

        var result = await Send();

        Assert.Equal(new Error(409, "Slot is full."), result.Error);
        VerifyStored(409);
        _availability.Verify(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyClaimRemoved(Times.Never());
    }

    [Fact]
    public async Task Availabilitys_404_On_Reserve_Is_Stored()
    {
        _availability.Setup(a => a.ReserveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(404, "Slot not found.")));

        var result = await Send();

        Assert.Equal(404, result.Error!.Status);
        VerifyStored(404);
    }

    [Fact]
    public async Task A_503_Reading_The_Slot_Removes_The_Claim_And_Stores_Nothing()
    {
        _availability.Setup(a => a.GetSlotAsync(_slot.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SlotInfo>.Failure(new Error(503, "down")));

        var result = await Send();

        Assert.Equal(503, result.Error!.Status);
        VerifyClaimRemoved(Times.Once());
        VerifyNothingStored();
        _availability.Verify(a => a.ReserveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _availability.Verify(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_503_On_Reserve_Releases_Once_Leaves_The_Claim_In_Progress_And_Stores_Nothing()
    {
        _availability.Setup(a => a.ReserveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(503, "down")));

        var result = await Send();

        Assert.Equal(503, result.Error!.Status);
        _availability.Verify(a => a.ReleaseAsync(_slot.Id, _record.AppointmentId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyClaimRemoved(Times.Never());
        VerifyNothingStored();
        _appointments.Verify(r => r.TryAddBookedAsync(It.IsAny<Appointment>(), It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Failing_Compensating_Release_Is_Tolerated_After_A_Reserve_503()
    {
        _availability.Setup(a => a.ReserveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(503, "down")));
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(503, "down")));

        var result = await Send();

        Assert.Equal(503, result.Error!.Status);
        VerifyClaimRemoved(Times.Never());
    }

    [Fact]
    public async Task A_Retry_Whose_Claim_Carries_An_Earlier_Appointment_Id_Reserves_With_That_Id()
    {
        var earlier = Guid.CreateVersion7();
        var takenOver = IdempotencyRecord.Claim(_command.CustomerId, "key-1", "hash", earlier, Now.UtcDateTime);
        _idempotency.Setup(r => r.ClaimAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimResult.Claimed(takenOver));

        var result = await Send();

        Assert.Equal(earlier, result.Value!.Id);
        _availability.Verify(a => a.ReserveAsync(_slot.Id, earlier, It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(r => r.TryAddBookedAsync(It.Is<Appointment>(a => a.Id == earlier), takenOver, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Save_Exception_Releases_Leaves_The_Claim_And_Rethrows()
    {
        _appointments.Setup(r => r.TryAddBookedAsync(It.IsAny<Appointment>(), It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Send());

        _availability.Verify(a => a.ReleaseAsync(_slot.Id, _record.AppointmentId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyClaimRemoved(Times.Never());
        VerifyNothingStored();
    }

    [Fact]
    public async Task An_Exception_Before_The_Reserve_Removes_The_Claim_And_Rethrows()
    {
        _appointments.Setup(r => r.HasOverlappingBookedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Send());

        VerifyClaimRemoved(Times.Once());
        _availability.Verify(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Overlap_At_Save_Releases_And_Stores_409()
    {
        _appointments.Setup(r => r.TryAddBookedAsync(It.IsAny<Appointment>(), It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddAppointmentOutcome.Overlap);

        var result = await Send();

        Assert.Equal(409, result.Error!.Status);
        _availability.Verify(a => a.ReleaseAsync(_slot.Id, _record.AppointmentId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyStored(409);
    }

    [Fact]
    public async Task An_Overlap_At_Save_Whose_Release_Fails_Is_503_With_The_Claim_Left_In_Progress()
    {
        _appointments.Setup(r => r.TryAddBookedAsync(It.IsAny<Appointment>(), It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddAppointmentOutcome.Overlap);
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(503, "down")));

        var result = await Send();

        Assert.Equal(503, result.Error!.Status);
        _availability.Verify(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyNothingStored();
        VerifyClaimRemoved(Times.Never());
    }
}
