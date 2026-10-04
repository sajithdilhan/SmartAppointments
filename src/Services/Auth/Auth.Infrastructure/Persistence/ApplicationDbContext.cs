using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email)
                .HasConversion(
                    email => email.Value,
                    value => Email.Create(value))
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.PhoneNumber).IsRequired(); 
            entity.Property(e => e.RegistrationDateUtc).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();

            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.ExpiresAtUtc).IsRequired();
            entity.Property(e => e.FamilyStartedAtUtc).IsRequired();

            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.FamilyId);

            // ReplacedById is bookkeeping, not an integrity rule: no foreign key, so a later purge
            // of old rows cannot trip over it.
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        });
    }

    public DbSet<User> Users { get; set; }

    public DbSet<RefreshToken> RefreshTokens { get; set; }
}
