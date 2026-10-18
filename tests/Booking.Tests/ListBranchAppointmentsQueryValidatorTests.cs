using Booking.Application.Queries;
using Booking.Application.Validations;
using SmartAppointments.BuildingBlocks;

namespace Booking.Tests;

public class ListBranchAppointmentsQueryValidatorTests
{
    private static ListBranchAppointmentsQuery Query(
        string? date = null, string? status = null, string? page = null, string? pageSize = null) =>
        new(Guid.CreateVersion7(), date, status, page, pageSize, Constants.AdminRole, null);

    [Fact]
    public void No_Parameters_Is_Valid()
    {
        Assert.True(new ListBranchAppointmentsQueryValidator().Validate(Query()).IsValid);
    }

    [Fact]
    public void All_Valid_Parameters_Pass()
    {
        Assert.True(new ListBranchAppointmentsQueryValidator()
            .Validate(Query("2030-03-10", "NoShow", "2", "100")).IsValid);
    }

    [Fact]
    public void Every_Failure_Is_Reported_Together()
    {
        var result = new ListBranchAppointmentsQueryValidator().Validate(Query("10-03-2030", "Pending", "0", "101"));

        Assert.False(result.IsValid);
        var messages = result.Errors.Select(e => e.ErrorMessage).ToList();
        Assert.Contains("The date must be a valid date in the format yyyy-MM-dd.", messages);
        Assert.Contains("The status must be Booked, Cancelled, Completed or NoShow.", messages);
        Assert.Contains("The page must be a whole number of at least 1.", messages);
        Assert.Contains("The pageSize must be a whole number from 1 to 100.", messages);
    }

    [Fact]
    public void An_Empty_Date_Is_Invalid()
    {
        Assert.False(new ListBranchAppointmentsQueryValidator().Validate(Query(date: "")).IsValid);
    }
}
