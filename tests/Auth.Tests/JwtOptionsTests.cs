using Auth.Application.Models;

namespace Auth.Tests;

public class JwtOptionsTests
{
    [Fact]
    public void Accessors_Return_The_Configured_Spans()
    {
        var options = new JwtOptions { RefreshTokenExpirationDays = 7, RefreshTokenFamilyMaxDays = 30 };

        Assert.Equal(TimeSpan.FromDays(7), options.GetRefreshTokenLifetime());
        Assert.Equal(TimeSpan.FromDays(30), options.GetRefreshTokenFamilyMaxLifetime());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Lifetime_Accessor_Throws_For_A_Non_Positive_Value(int days)
    {
        var options = new JwtOptions { RefreshTokenExpirationDays = days };

        Assert.Throws<InvalidOperationException>(() => options.GetRefreshTokenLifetime());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Family_Max_Accessor_Throws_For_A_Non_Positive_Value(int days)
    {
        var options = new JwtOptions { RefreshTokenFamilyMaxDays = days };

        Assert.Throws<InvalidOperationException>(() => options.GetRefreshTokenFamilyMaxLifetime());
    }

    [Fact]
    public void Family_Max_Defaults_To_Thirty_Days()
    {
        Assert.Equal(30, new JwtOptions().RefreshTokenFamilyMaxDays);
    }

    [Theory]
    [InlineData(7, 30, true)]
    [InlineData(7, 7, true)]
    [InlineData(7, 6, false)]
    [InlineData(0, 30, false)]
    [InlineData(-1, 30, false)]
    public void Startup_Validation_Requires_A_Cap_That_Covers_The_Sliding_Window(int days, int maxDays, bool expected)
    {
        var options = new JwtOptions { RefreshTokenExpirationDays = days, RefreshTokenFamilyMaxDays = maxDays };

        Assert.Equal(expected, JwtOptions.AreRefreshSettingsValid(options));
    }
}
