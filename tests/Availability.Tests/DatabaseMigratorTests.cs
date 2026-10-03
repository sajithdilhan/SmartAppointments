using Availability.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Tests;

public class DatabaseMigratorTests
{
    private static IConfiguration Configure(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Migrations_Are_Disabled_When_The_Flag_Is_Absent()
    {
        Assert.False(DatabaseMigrator.IsEnabled(Configure()));
    }

    [Fact]
    public void Migrations_Are_Disabled_When_The_Flag_Is_False()
    {
        Assert.False(DatabaseMigrator.IsEnabled(Configure((DatabaseMigrator.MigrateOnStartupKey, "false"))));
    }

    [Fact]
    public void Migrations_Are_Enabled_When_The_Flag_Is_True()
    {
        Assert.True(DatabaseMigrator.IsEnabled(Configure((DatabaseMigrator.MigrateOnStartupKey, "true"))));
    }

    [Fact]
    public async Task With_The_Flag_Off_The_Migrator_Completes_Without_Resolving_The_DbContext()
    {
        // No ApplicationDbContext is registered, so resolving it would throw.
        var services = new ServiceCollection()
            .AddSingleton(Configure())
            .AddLogging()
            .BuildServiceProvider();

        await DatabaseMigrator.MigrateIfEnabledAsync(services);
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void The_Checked_In_Settings_Do_Not_Turn_Migrations_On(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ApiSettings", fileName);
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

        Assert.False(DatabaseMigrator.IsEnabled(configuration));
    }
}
