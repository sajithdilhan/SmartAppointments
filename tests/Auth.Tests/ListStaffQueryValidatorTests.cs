using Auth.Application.Queries;
using Auth.Application.Validations;

namespace Auth.Tests;

public class ListStaffQueryValidatorTests
{
    private static readonly ListStaffQuery Omitted = new(null, null, null, null);

    private readonly ListStaffQueryValidator _validator = new();

    [Fact]
    public void All_Parameters_Omitted_Passes()
    {
        Assert.True(_validator.Validate(Omitted).IsValid);
    }

    [Fact]
    public void All_Parameters_Valid_Passes()
    {
        var query = new ListStaffQuery(Guid.CreateVersion7().ToString(), "False", "2", "100");

        Assert.True(_validator.Validate(query).IsValid);
    }

    [Theory]
    [InlineData("branchId", "xyz", "The branchId must be a valid, non-empty GUID.")]
    [InlineData("branchId", "", "The branchId must be a valid, non-empty GUID.")]
    [InlineData("branchId", "00000000-0000-0000-0000-000000000000", "The branchId must be a valid, non-empty GUID.")]
    [InlineData("isActive", "1", "The isActive must be true or false.")]
    [InlineData("isActive", "", "The isActive must be true or false.")]
    [InlineData("page", "0", "The page must be a whole number of at least 1.")]
    [InlineData("page", "+1", "The page must be a whole number of at least 1.")]
    [InlineData("pageSize", "101", "The pageSize must be a whole number from 1 to 100.")]
    [InlineData("pageSize", "", "The pageSize must be a whole number from 1 to 100.")]
    public void Each_Rule_Fails_Alone_With_Its_Message(string parameter, string value, string message)
    {
        var query = parameter switch
        {
            "branchId" => Omitted with { BranchId = value },
            "isActive" => Omitted with { IsActive = value },
            "page" => Omitted with { Page = value },
            _ => Omitted with { PageSize = value }
        };

        var result = _validator.Validate(query);

        Assert.Equal(message, Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void Several_Bad_Parameters_Yield_Several_Errors()
    {
        var result = _validator.Validate(new ListStaffQuery("xyz", "x", "0", null));

        Assert.Equal(3, result.Errors.Count);
    }
}
