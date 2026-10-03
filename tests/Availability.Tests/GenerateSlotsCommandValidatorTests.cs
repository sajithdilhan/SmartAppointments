using Availability.Application.Commands;
using Availability.Application.Validations;

namespace Availability.Tests;

public class GenerateSlotsCommandValidatorTests
{
    private static readonly GenerateSlotsCommand Valid = new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2030, 1, 7), new DateOnly(2030, 1, 13), 5);

    private readonly GenerateSlotsCommandValidator _validator = new();

    [Fact]
    public void A_Valid_Command_Passes()
    {
        Assert.True(_validator.Validate(Valid).IsValid);
    }

    [Fact]
    public void A_Single_Day_Range_Passes()
    {
        Assert.True(_validator.Validate(Valid with { ToDate = Valid.FromDate }).IsValid);
    }

    [Fact]
    public void A_Range_Spanning_31_Days_Passes_And_32_Is_Rejected()
    {
        // DayNumber difference 30 is 31 dates inclusive.
        Assert.True(_validator.Validate(Valid with { ToDate = Valid.FromDate.AddDays(30) }).IsValid);

        var tooLong = _validator.Validate(Valid with { ToDate = Valid.FromDate.AddDays(31) });
        Assert.False(tooLong.IsValid);
        Assert.Contains(tooLong.Errors, e => e.ErrorMessage.Contains("31 days"));
    }

    [Fact]
    public void From_After_To_Is_Rejected()
    {
        var result = _validator.Validate(Valid with { FromDate = Valid.ToDate.AddDays(1) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("after"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [InlineData(-1, false)]
    public void Capacity_Must_Be_Between_1_And_100(int capacity, bool valid)
    {
        Assert.Equal(valid, _validator.Validate(Valid with { Capacity = capacity }).IsValid);
    }

    [Fact]
    public void Empty_Ids_Are_Rejected()
    {
        Assert.False(_validator.Validate(Valid with { BranchId = Guid.Empty }).IsValid);
        Assert.False(_validator.Validate(Valid with { ServiceTypeId = Guid.Empty }).IsValid);
    }
}
