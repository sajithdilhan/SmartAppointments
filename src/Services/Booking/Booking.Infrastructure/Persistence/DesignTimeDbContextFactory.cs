using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Booking.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the API host, which refuses to start
/// without secrets. <c>migrations add</c> never opens a connection, so it works on the placeholder;
/// <c>database update</c> needs the real connection string from the API project's appsettings,
/// user-secrets or the environment.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    // Must match <UserSecretsId> in Booking.Api.csproj.
    private const string ApiUserSecretsId = "7c1e4b90-5d3a-4f62-9a8e-b1d0c6f2a847";

    private const string PlaceholderConnectionString = "Host=localhost;Database=design_time_placeholder";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // dotnet ef runs with the startup project (Booking.Api) as the working directory, so its
        // appsettings files resolve from there. Later sources override earlier ones, as in the host.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = PlaceholderConnectionString;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
