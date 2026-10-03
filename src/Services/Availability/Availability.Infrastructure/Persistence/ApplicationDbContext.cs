using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("Branches");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Address).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(16);
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.TimeZoneId).HasMaxLength(64);

            entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName(BranchRepository.CodeIndexName);

            entity.OwnsMany(e => e.WorkingHours, hours =>
            {
                hours.ToTable("BranchWorkingHours");
                hours.WithOwner().HasForeignKey("BranchId");
                hours.HasKey("BranchId", nameof(WorkingHours.DayOfWeek));
                hours.Property(h => h.DayOfWeek).IsRequired();
                hours.Property(h => h.OpensAt).IsRequired();
                hours.Property(h => h.ClosesAt).IsRequired();
            });
            entity.Navigation(e => e.WorkingHours).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.ToTable("ServiceTypes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(30);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.DurationMinutes).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName(ServiceTypeRepository.CodeIndexName);
        });

        modelBuilder.Entity<Slot>(entity =>
        {
            entity.ToTable("Slots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LocalDate).IsRequired();
            entity.Property(e => e.StartUtc).IsRequired();
            entity.Property(e => e.EndUtc).IsRequired();
            entity.Property(e => e.Capacity).IsRequired();
            entity.Property(e => e.ReservedCount).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            // Restrict: a branch or service type with slots must not disappear under them.
            entity.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ServiceType>().WithMany().HasForeignKey(e => e.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.BranchId, e.ServiceTypeId, e.StartUtc })
                .IsUnique()
                .HasDatabaseName(SlotRepository.StartIndexName);
            entity.HasIndex(e => new { e.BranchId, e.ServiceTypeId, e.LocalDate })
                .HasDatabaseName(SlotRepository.LocalDateIndexName);
        });
    }

    public DbSet<Branch> Branches { get; set; }

    public DbSet<ServiceType> ServiceTypes { get; set; }

    public DbSet<Slot> Slots { get; set; }
}
