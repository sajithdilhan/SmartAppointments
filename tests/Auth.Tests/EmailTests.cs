using Auth.Domain.ValueObjects;

namespace Auth.Tests;

public class EmailTests
{
    [Theory]
    [InlineData("CAM@EXAMPLE.COM", "cam@example.com")]
    [InlineData("  cam@example.com  ", "cam@example.com")]
    [InlineData(" Cam.Customer@Example.Com ", "cam.customer@example.com")]
    public void Create_Trims_And_Lowercases(string input, string expected)
    {
        Assert.Equal(expected, Email.Create(input).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_Rejects_A_Blank_Value(string? input)
    {
        Assert.Throws<ArgumentException>(() => Email.Create(input!));
    }

    [Fact]
    public void Create_Rejects_A_Value_Without_An_At_Sign()
    {
        Assert.Throws<ArgumentException>(() => Email.Create("cam.example.com"));
    }

    [Fact]
    public void Two_Emails_With_Different_Casing_Are_Equal()
    {
        // Value equality is what makes the `u.Email == value` comparison in UserRepository work,
        // and normalisation is what makes a differently-cased lookup find the stored row.
        Assert.Equal(Email.Create("cam@example.com"), Email.Create("CAM@Example.com "));
    }

    [Fact]
    public void ToString_Returns_The_Normalised_Value()
    {
        Assert.Equal("cam@example.com", Email.Create("Cam@Example.com").ToString());
    }
}
