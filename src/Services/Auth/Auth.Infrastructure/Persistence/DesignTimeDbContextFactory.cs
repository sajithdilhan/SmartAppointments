using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the API host, which refuses to start
/// without secrets. <c>migrations add</c> never opens a connection, so it works on the placeholder;
/// <c>database update</c> needs the real connection string from the API project's appsettings,
/// user-secrets or the environment.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    // Must match <UserSecretsId> in Auth.Api.csproj.
    private const string ApiUserSecretsId = "5f16a25f-b40d-46d7-abe4-1275bd1f94db";

    private const string PlaceholderConnectionString = "Host=localhost;Database=design_time_placeholder";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // dotnet ef runs with the startup project (Auth.Api) as the working directory, so its
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
