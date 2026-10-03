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
}
