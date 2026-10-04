using Microsoft.Extensions.Configuration;
using SmartAppointments.Gateway.Configuration;

namespace Gateway.Tests;

public class CorsConfigurationTests
{
    private static IConfigurationRoot Shipped() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .Build();

    private static IConfigurationRoot Development() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Development.json", optional: false)
        .Build();

    [Fact]
    public void The_Shipped_Configuration_Allows_No_Origin()
    {
        Assert.Empty(Shipped().GetSection("Cors:AllowedOrigins").GetChildren());
        Assert.Empty(CorsOriginsValidator.Validate(Shipped()));
    }

    [Fact]
    public void Development_Allows_Exactly_The_Two_Local_Web_Origins_And_They_Are_Valid()
    {
        var origins = CorsOriginsValidator.Validate(Development());

        Assert.Equal(["http://localhost:4200", "http://localhost:8081"], origins);
    }
}
