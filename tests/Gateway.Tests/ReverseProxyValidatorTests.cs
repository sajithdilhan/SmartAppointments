using Microsoft.Extensions.Configuration;
using SmartAppointments.Gateway.Configuration;

namespace Gateway.Tests;

public class ReverseProxyValidatorTests
{
    private static Dictionary<string, string?> Valid() => new()
    {
        ["ReverseProxy:Clusters:auth:Destinations:primary:Address"] = "http://localhost:5008",
        ["ReverseProxy:Clusters:availability:Destinations:primary:Address"] = "http://localhost:5202",
        ["ReverseProxy:Clusters:booking:Destinations:primary:Address"] = "https://booking.internal:8443",
    };

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void A_Valid_Configuration_Passes()
    {
        ReverseProxyValidator.Validate(Build(Valid()));
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("availability")]
    [InlineData("booking")]
    public void A_Missing_Cluster_Throws_Naming_The_Key(string cluster)
    {
        var values = Valid();
        values.Remove($"ReverseProxy:Clusters:{cluster}:Destinations:primary:Address");

        var ex = Assert.Throws<InvalidOperationException>(() => ReverseProxyValidator.Validate(Build(values)));

        Assert.Contains($"ReverseProxy:Clusters:{cluster}:Destinations", ex.Message);
        Assert.Contains($"ReverseProxy__Clusters__{cluster}__Destinations__primary__Address", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost:5008")]
    [InlineData("/relative/path")]
    [InlineData("ftp://localhost:21")]
    [InlineData("not a url")]
    public void A_Blank_Relative_Or_Non_Http_Address_Throws_Naming_The_Key(string address)
    {
        var values = Valid();
        values["ReverseProxy:Clusters:availability:Destinations:primary:Address"] = address;

        var ex = Assert.Throws<InvalidOperationException>(() => ReverseProxyValidator.Validate(Build(values)));

        Assert.Contains("ReverseProxy:Clusters:availability:Destinations:primary:Address", ex.Message);
        Assert.Contains("ReverseProxy__Clusters__availability__Destinations__primary__Address", ex.Message);
    }

    [Fact]
    public void A_Cluster_With_No_Destination_Throws()
    {
        var values = Valid();
        values.Remove("ReverseProxy:Clusters:auth:Destinations:primary:Address");
        values["ReverseProxy:Clusters:auth:HttpRequest:ActivityTimeout"] = "00:00:30";

        var ex = Assert.Throws<InvalidOperationException>(() => ReverseProxyValidator.Validate(Build(values)));

        Assert.Contains("ReverseProxy:Clusters:auth:Destinations", ex.Message);
    }

    [Fact]
    public void The_Shipped_Configuration_Alone_Fails_Because_Its_Addresses_Are_Blank()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Assert.Throws<InvalidOperationException>(() => ReverseProxyValidator.Validate(configuration));
    }

    [Fact]
    public void The_Development_Configuration_Passes()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        ReverseProxyValidator.Validate(configuration);
    }
}
