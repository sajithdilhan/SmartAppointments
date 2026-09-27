using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Availability.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the API host, which refuses to start
/// without secrets. <c>migrations add</c> never opens a connection, so it works on the placeholder;
/// <c>database update</c> needs the real connection string from user-secrets or the environment.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    // Must match <UserSecretsId> in Availability.Api.csproj.
    private const string ApiUserSecretsId = "2a5a3358-b9c5-4360-b47b-893871328dad";

    private const string PlaceholderConnectionString = "Host=localhost;Database=design_time_placeholder";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
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
