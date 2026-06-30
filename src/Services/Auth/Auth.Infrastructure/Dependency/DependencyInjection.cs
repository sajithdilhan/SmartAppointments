using Auth.Application.Abstractions;
using Auth.Application.Models;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Infrastructure.Dependency;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        // Register infrastructure services here
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.Configure<JwtOptions>(configuration.GetRequiredSection("Jwt"));
        services.AddScoped<ITokenGenerator, TokenGenerator>();
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddHealthChecks().AddNpgSql(connectionString);

        return services;
    }
}
