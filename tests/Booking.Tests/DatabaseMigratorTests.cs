using Booking.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Tests;

public class DatabaseMigratorTests
{
    private static IConfiguration Build(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void IsEnabled_Is_False_When_Absent() =>
        Assert.False(DatabaseMigrator.IsEnabled(Build()));

    [Fact]
    public void IsEnabled_Is_False_For_False() =>
        Assert.False(DatabaseMigrator.IsEnabled(Build((DatabaseMigrator.MigrateOnStartupKey, "false"))));

    [Fact]
    public void IsEnabled_Is_True_For_True() =>
        Assert.True(DatabaseMigrator.IsEnabled(Build((DatabaseMigrator.MigrateOnStartupKey, "true"))));

    [Fact]
    public async Task MigrateIfEnabledAsync_Skips_Without_Resolving_The_DbContext_When_Disabled()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Build());
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        // No ApplicationDbContext is registered, so resolving it would throw.
        await DatabaseMigrator.MigrateIfEnabledAsync(provider);
    }
}
