using Booking.Application.Queries;
using Booking.Application.Validations;

namespace Booking.Tests;

public class ListMyAppointmentsQueryValidatorTests
{
    private static readonly ListMyAppointmentsQuery Omitted = new(Guid.CreateVersion7(), null, null, null, null);

    private readonly ListMyAppointmentsQueryValidator _validator = new();

    [Fact]
    public void All_Parameters_Omitted_Passes()
    {
        Assert.True(_validator.Validate(Omitted).IsValid);
    }

    [Fact]
    public void All_Parameters_Valid_Passes()
    {
        var query = Omitted with { Status = "booked", When = "Upcoming", Page = "2", PageSize = "100" };

        Assert.True(_validator.Validate(query).IsValid);
    }

    [Theory]
    [InlineData("status", "Completed", "The status must be Booked or Cancelled.")]
    [InlineData("status", "", "The status must be Booked or Cancelled.")]
    [InlineData("when", "now", "The when must be upcoming or past.")]
    [InlineData("when", "", "The when must be upcoming or past.")]
    [InlineData("page", "0", "The page must be a whole number of at least 1.")]
    [InlineData("page", "abc", "The page must be a whole number of at least 1.")]
    [InlineData("pageSize", "101", "The pageSize must be a whole number from 1 to 100.")]
    [InlineData("pageSize", "", "The pageSize must be a whole number from 1 to 100.")]
    public void Each_Rule_Fails_Alone_With_Its_Message(string parameter, string value, string message)
    {
        var query = parameter switch
        {
            "status" => Omitted with { Status = value },
            "when" => Omitted with { When = value },
            "page" => Omitted with { Page = value },
            _ => Omitted with { PageSize = value }
        };

        var result = _validator.Validate(query);

        Assert.Equal(message, Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void Several_Bad_Parameters_Yield_Several_Errors()
    {
        var query = Omitted with { Status = "x", When = "y", Page = "0", PageSize = "500" };

        var result = _validator.Validate(query);

        Assert.Equal(4, result.Errors.Count);
    }
}
