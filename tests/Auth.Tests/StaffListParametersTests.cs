using Auth.Application.Models;

namespace Auth.Tests;

public class StaffListParametersTests
{
    private static readonly Guid Branch = Guid.CreateVersion7();

    [Fact]
    public void BranchId_Omitted_Is_No_Filter()
    {
        Assert.True(StaffListParameters.TryParseBranchId(null, out var branchId));
        Assert.Null(branchId);
    }

    [Fact]
    public void BranchId_Valid_Guid_Is_Parsed()
    {
        Assert.True(StaffListParameters.TryParseBranchId(Branch.ToString(), out var branchId));
        Assert.Equal(Branch, branchId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void BranchId_Invalid_Is_Rejected(string raw)
    {
        Assert.False(StaffListParameters.TryParseBranchId(raw, out _));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    [InlineData("false", false)]
    public void IsActive_True_Or_False_Any_Case_Is_Parsed(string raw, bool expected)
    {
        Assert.True(StaffListParameters.TryParseIsActive(raw, out var isActive));
        Assert.Equal(expected, isActive);
    }

    [Fact]
    public void IsActive_Omitted_Is_No_Filter()
    {
        Assert.True(StaffListParameters.TryParseIsActive(null, out var isActive));
        Assert.Null(isActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData(" true")]
    [InlineData("true ")]
    public void IsActive_Anything_Else_Is_Rejected(string raw)
    {
        Assert.False(StaffListParameters.TryParseIsActive(raw, out _));
    }

    [Fact]
    public void Page_And_PageSize_Default_When_Omitted()
    {
        Assert.True(StaffListParameters.TryParsePage(null, out var page));
        Assert.True(StaffListParameters.TryParsePageSize(null, out var pageSize));
        Assert.Equal(1, page);
        Assert.Equal(20, pageSize);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("7", 7)]
    [InlineData("2147483647", int.MaxValue)]
    public void Page_Accepts_Whole_Numbers_From_One(string raw, int expected)
    {
        Assert.True(StaffListParameters.TryParsePage(raw, out var page));
        Assert.Equal(expected, page);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("1e1")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public void Page_Rejects_Anything_Else(string raw)
    {
        Assert.False(StaffListParameters.TryParsePage(raw, out _));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    public void PageSize_Accepts_One_To_The_Maximum(string raw, int expected)
    {
        Assert.True(StaffListParameters.TryParsePageSize(raw, out var pageSize));
        Assert.Equal(expected, pageSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    public void PageSize_Rejects_Anything_Else(string raw)
    {
        Assert.False(StaffListParameters.TryParsePageSize(raw, out _));
    }
}
