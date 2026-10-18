using System.Text.Json;
using System.Text.Json.Serialization;
using Booking.Application.Models;
using Booking.Domain.Entities;

namespace Booking.Tests;

public class AppointmentResponseTests
{
    private static readonly DateTime Now = new(2030, 1, 6, 12, 0, 0, DateTimeKind.Utc);

    private static Appointment Book() => Appointment.Book(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        Now, Now.AddMinutes(30), Now.AddDays(-2));

    private static JsonElement Serialise(AppointmentResponse response)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.SerializeToElement(response, options);
    }

    [Fact]
    public void A_Completed_Appointment_Maps_CompletedAtUtc_And_Serialises_The_Status_As_A_String()
    {
        var response = AppointmentResponse.From(Book().WithStatus(AppointmentStatus.Completed, Now));

        Assert.Equal(Now, response.CompletedAtUtc);
        Assert.Null(response.NoShowAtUtc);
        Assert.Null(response.CancelledAtUtc);
        Assert.Equal("Completed", Serialise(response).GetProperty("status").GetString());
    }

    [Fact]
    public void A_NoShow_Appointment_Maps_NoShowAtUtc_And_Serialises_The_Status_As_A_String()
    {
        var response = AppointmentResponse.From(Book().WithStatus(AppointmentStatus.NoShow, Now));

        Assert.Equal(Now, response.NoShowAtUtc);
        Assert.Null(response.CompletedAtUtc);
        Assert.Null(response.CancelledAtUtc);
        Assert.Equal("NoShow", Serialise(response).GetProperty("status").GetString());
    }

    [Fact]
    public void A_Booked_Appointment_Has_Null_Mark_Timestamps()
    {
        var response = AppointmentResponse.From(Book());

        Assert.Null(response.CompletedAtUtc);
        Assert.Null(response.NoShowAtUtc);
        var json = Serialise(response);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("completedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("noShowAtUtc").ValueKind);
    }
}
