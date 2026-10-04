using Microsoft.Extensions.Configuration;
using SmartAppointments.Gateway.Configuration;

namespace Gateway.Tests;

public class CorsOriginsValidatorTests
{
    private static IConfiguration Build(params string?[] origins)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < origins.Length; i++)
        {
            values[$"Cors:AllowedOrigins:{i}"] = origins[i];
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("https://app.example.com")]
    [InlineData("https://app.example.com:8443")]
    [InlineData("http://[::1]:4200")]
    public void A_Valid_Origin_Is_Returned(string origin)
    {
        Assert.Equal([origin], CorsOriginsValidator.Validate(Build(origin)));
    }

    [Fact]
    public void An_Absent_Section_Returns_An_Empty_List()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Empty(CorsOriginsValidator.Validate(configuration));
    }

    [Fact]
    public void An_Empty_Section_Returns_An_Empty_List()
    {
        Assert.Empty(CorsOriginsValidator.Validate(Build()));
    }

    [Fact]
    public void Blank_And_Whitespace_Entries_Are_Dropped()
    {
        Assert.Empty(CorsOriginsValidator.Validate(Build("", "   ", null)));
    }

    [Fact]
    public void A_Blank_Entry_Beside_A_Valid_One_Returns_The_Valid_One()
    {
        Assert.Equal(["http://localhost:8081"], CorsOriginsValidator.Validate(Build("", "http://localhost:8081")));
    }

    [Fact]
    public void Duplicates_Are_Collapsed_And_Order_Is_Kept()
    {
        var result = CorsOriginsValidator.Validate(
            Build("http://localhost:8081", "http://localhost:4200", "http://localhost:8081"));

        Assert.Equal(["http://localhost:8081", "http://localhost:4200"], result);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("localhost:4200")]
    [InlineData("/path")]
    [InlineData("not a url")]
    [InlineData("ftp://host")]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/path")]
    [InlineData("http://localhost:4200?x=1")]
    [InlineData("http://localhost:4200#f")]
    [InlineData("http://user:pw@localhost")]
    [InlineData("HTTP://localhost:4200")]
    [InlineData("http://localhost:80")]
    public void A_Malformed_Origin_Throws_Naming_The_Key(string origin)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CorsOriginsValidator.Validate(Build(origin)));

        Assert.Contains("Cors:AllowedOrigins:0", ex.Message);
        Assert.Contains("Cors__AllowedOrigins__0", ex.Message);
    }

    [Fact]
    public void A_Bad_Entry_After_A_Good_One_Names_Its_Own_Index()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorsOriginsValidator.Validate(Build("http://localhost:4200", "https://app.example.com/")));

        Assert.Contains("Cors:AllowedOrigins:1", ex.Message);
        Assert.Contains("Cors__AllowedOrigins__1", ex.Message);
    }
}
