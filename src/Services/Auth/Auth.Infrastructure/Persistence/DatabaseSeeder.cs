using Auth.Application.Abstractions;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Enums;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Creates the bootstrap Admin account. Without it no Admin or Staff identity can ever exist,
/// because registration only produces Customers and the Admin-only endpoints have no other way in.
/// </summary>
public static class DatabaseSeeder
{
    private const string AdminSeedSection = "Seed:Admin";

    public static async Task SeedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseSeeder));
        var configuration = services.GetRequiredService<IConfiguration>();

        var section = configuration.GetSection(AdminSeedSection);
        var email = section["Email"];
        var password = section["Password"];
        var firstName = section["FirstName"];
        var lastName = section["LastName"];
        var phoneNumber = section["PhoneNumber"];

        // Inert unless fully configured, so environments that don't want a seeded Admin get one by omission.
        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(firstName)
            || string.IsNullOrWhiteSpace(lastName)
            || string.IsNullOrWhiteSpace(phoneNumber))
        {
            logger.LogInformation("Admin seed configuration is absent or incomplete; skipping seeding.");
            return;
        }

        var context = services.GetRequiredService<ApplicationDbContext>();

        if (await context.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken))
        {
            logger.LogInformation("An Admin account already exists; skipping seeding.");
            return;
        }

        var passwordHasher = services.GetRequiredService<IPasswordHasher>();

        var admin = User.RegisterAdmin(
            firstName,
            lastName,
            Email.Create(email),
            phoneNumber,
            passwordHasher.Hash(password));

        await context.Users.AddAsync(admin, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded initial Admin account {Email}.", admin.Email.Value);
    }
}
