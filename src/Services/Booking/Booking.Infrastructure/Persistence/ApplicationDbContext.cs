using Booking.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.ToTable("Appointments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CustomerId).IsRequired();
            entity.Property(e => e.SlotId).IsRequired();
            entity.Property(e => e.BranchId).IsRequired();
            entity.Property(e => e.ServiceTypeId).IsRequired();
            entity.Property(e => e.StartUtc).IsRequired();
            entity.Property(e => e.EndUtc).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            // Serves the overlap check, and the future "my appointments" list.
            entity.HasIndex(e => new { e.CustomerId, e.StartUtc }).HasDatabaseName("IX_Appointments_CustomerId_StartUtc");
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.RequestHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.AppointmentId).IsRequired();
            entity.Property(e => e.State).IsRequired().HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.ResponseBody).HasColumnType("jsonb");
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.ExpiresAtUtc).IsRequired();

            // The database, not application logic, decides who wins two simultaneous first requests.
            entity.HasIndex(e => new { e.UserId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName(IdempotencyRepository.KeyIndexName);
        });
    }
}
