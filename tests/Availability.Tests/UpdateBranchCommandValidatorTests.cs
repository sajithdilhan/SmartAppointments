using Availability.Application.Commands;
using Availability.Application.Validations;

namespace Availability.Tests;

public class UpdateBranchCommandValidatorTests
{
    private static readonly UpdateBranchCommand Valid =
        new(Guid.CreateVersion7(), "Pettah", "Walk-ins welcome", "12 Main Street", "+94112345678");

    private readonly UpdateBranchCommandValidator _subject = new();

    [Fact]
    public void A_Fully_Valid_Command_Passes()
    {
        Assert.True(_subject.Validate(Valid).IsValid);
    }

    [Fact]
    public void A_Null_Description_Passes()
    {
        Assert.True(_subject.Validate(Valid with { Description = null }).IsValid);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Name_Is_At_Most_100_Characters(int length, bool valid)
    {
        Assert.Equal(valid, _subject.Validate(Valid with { Name = new string('a', length) }).IsValid);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Description_Is_At_Most_500_Characters(int length, bool valid)
    {
        Assert.Equal(valid, _subject.Validate(Valid with { Description = new string('a', length) }).IsValid);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Address_Is_At_Most_200_Characters(int length, bool valid)
    {
        Assert.Equal(valid, _subject.Validate(Valid with { Address = new string('a', length) }).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0112345678")]
    [InlineData("+94 11 234")]
    [InlineData("phone")]
    public void Invalid_Phone_Numbers_Are_Rejected(string phoneNumber)
    {
        Assert.False(_subject.Validate(Valid with { PhoneNumber = phoneNumber }).IsValid);
    }

    [Fact]
    public void Every_Failed_Rule_Is_Reported()
    {
        var result = _subject.Validate(Valid with { Name = "", Address = "", PhoneNumber = "" });

        var messages = result.Errors.Select(e => e.ErrorMessage).ToList();
        Assert.Contains("Name is required.", messages);
        Assert.Contains("Address is required.", messages);
        Assert.Contains("Phone number is required.", messages);
    }
}
