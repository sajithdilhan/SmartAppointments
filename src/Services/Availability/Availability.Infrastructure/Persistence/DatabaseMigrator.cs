using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations at start-up when <c>Database:MigrateOnStartup</c> is true.
/// Off by default, so no shipped settings file changes the schema of a database on its own.
/// </summary>
public static class DatabaseMigrator
{
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>An absent key is false; an unparseable value throws, which is a deliberate loud failure.</summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>(MigrateOnStartupKey);

    public static async Task MigrateIfEnabledAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseMigrator));
        var configuration = services.GetRequiredService<IConfiguration>();

        // Return before the DbContext is resolved, so a disabled migrator never touches the database.
        if (!IsEnabled(configuration))
        {
            logger.LogInformation("{Key} is not enabled; skipping database migrations.", MigrateOnStartupKey);
            return;
        }

        var context = services.GetRequiredService<ApplicationDbContext>();

        try
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
            logger.LogInformation("Applying {Count} pending database migration(s).", pending);

            await context.Database.MigrateAsync(cancellationToken);

            logger.LogInformation("Database migrations are up to date.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Could not apply database migrations.");
            throw;
        }
    }
}
