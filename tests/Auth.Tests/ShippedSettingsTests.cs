using Microsoft.Extensions.Configuration;

namespace Auth.Tests;

public class ShippedSettingsTests
{
    private static IConfiguration Load(string file) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ApiSettings", file), optional: false)
            .Build();

    [Theory]
    [InlineData("InternalApi:Key")]
    [InlineData("Services:Availability:BaseUrl")]
    public void The_Base_Settings_Ship_The_Internal_Call_Keys_Empty(string key)
    {
        var configuration = Load("appsettings.json");

        Assert.True(configuration.GetSection(key).Exists());
        Assert.Equal(string.Empty, configuration[key]);
    }

    [Fact]
    public void The_Development_Settings_Carry_The_Shared_Key_And_The_Local_Availability_Address()
    {
        var development = Load("appsettings.Development.json");

        Assert.True(development["InternalApi:Key"]!.Length >= 32);
        Assert.Equal("http://localhost:5202", development["Services:Availability:BaseUrl"]);
    }
}
