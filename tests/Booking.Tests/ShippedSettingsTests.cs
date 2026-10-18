using Booking.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Booking.Tests;

public class ShippedSettingsTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void Shipped_Settings_Do_Not_Enable_Migrate_On_Startup(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ApiSettings", file);
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

        Assert.False(DatabaseMigrator.IsEnabled(configuration));
    }

    [Fact]
    public void The_Auth_Base_Url_Ships_Empty()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ApiSettings", "appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

        Assert.Equal(string.Empty, configuration["Services:Auth:BaseUrl"]);
    }

    [Fact]
    public void The_Development_Auth_Base_Url_Is_The_Local_Auth_Service()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ApiSettings", "appsettings.Development.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

        Assert.Equal("http://localhost:5008", configuration["Services:Auth:BaseUrl"]);
    }
}
