using Auth.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Tests;

public class DatabaseMigratorTests
{
    private static IConfiguration BuildConfiguration(string? value)
    {
        var values = new Dictionary<string, string?>();
        if (value is not null)
        {
            values[DatabaseMigrator.MigrateOnStartupKey] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void IsEnabled_Is_False_When_Absent()
    {
        Assert.False(DatabaseMigrator.IsEnabled(BuildConfiguration(null)));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void IsEnabled_Reads_The_Flag(string value, bool expected)
    {
        Assert.Equal(expected, DatabaseMigrator.IsEnabled(BuildConfiguration(value)));
    }

    [Fact]
    public async Task MigrateIfEnabledAsync_Does_Not_Resolve_The_DbContext_When_Disabled()
    {
        // No ApplicationDbContext is registered, so resolving it would throw.
        var services = new ServiceCollection();
        services.AddSingleton(BuildConfiguration("false"));
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        await DatabaseMigrator.MigrateIfEnabledAsync(provider);
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void Shipped_Settings_Do_Not_Enable_Migration(string fileName)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ApiSettings", fileName), optional: false)
            .Build();

        Assert.False(DatabaseMigrator.IsEnabled(configuration));
    }
}
