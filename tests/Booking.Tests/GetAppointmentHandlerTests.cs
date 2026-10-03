using Booking.Application.Abstractions;
using Booking.Application.Handlers;
using Booking.Application.Queries;
using Booking.Domain.Entities;
using Moq;
using SmartAppointments.BuildingBlocks;

namespace Booking.Tests;

public class GetAppointmentHandlerTests
{
    private static readonly DateTime Now = new(2030, 1, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Guid _owner = Guid.CreateVersion7();
    private readonly Appointment _appointment;

    public GetAppointmentHandlerTests()
    {
        _appointment = Appointment.Book(
            Guid.CreateVersion7(), _owner, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Now.AddDays(1), Now.AddDays(1).AddMinutes(30), Now);
        _appointments.Setup(r => r.GetByIdNoTrackingAsync(_appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_appointment);
    }

    private Task<SmartAppointments.BuildingBlocks.Models.Result<Booking.Application.Models.AppointmentResponse>> Send(Guid id, Guid caller, string role) =>
        new GetAppointmentHandler(_appointments.Object).Handle(new GetAppointmentQuery(id, caller, role), CancellationToken.None);

    [Fact]
    public async Task The_Owner_Gets_The_Appointment()
    {
        var result = await Send(_appointment.Id, _owner, Constants.CustomerRole);

        Assert.True(result.IsSuccess);
        Assert.Equal(_appointment.Id, result.Value!.Id);
    }

    [Theory]
    [InlineData(Constants.StaffRole)]
    [InlineData(Constants.AdminRole)]
    public async Task Staff_And_Admin_Get_Any_Appointment(string role)
    {
        var result = await Send(_appointment.Id, Guid.CreateVersion7(), role);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Another_Customer_And_An_Unknown_Id_Get_The_Same_404_For_The_Requested_Id()
    {
        var unknownId = Guid.CreateVersion7();
        _appointments.Setup(r => r.GetByIdNoTrackingAsync(unknownId, It.IsAny<CancellationToken>())).ReturnsAsync((Appointment?)null);

        var other = await Send(_appointment.Id, Guid.CreateVersion7(), Constants.CustomerRole);
        var unknown = await Send(unknownId, Guid.CreateVersion7(), Constants.CustomerRole);

        Assert.Equal(404, other.Error!.Status);
        Assert.Equal(404, unknown.Error!.Status);
        Assert.Equal($"Appointment '{_appointment.Id}' was not found.", other.Error.Details);
        Assert.Equal($"Appointment '{unknownId}' was not found.", unknown.Error.Details);
    }
}
