using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Persistence;

public class ServiceTypeRepository(ApplicationDbContext context) : IServiceTypeRepository
{
    public const string CodeIndexName = "IX_ServiceTypes_Code";

    public async Task AddAsync(ServiceType serviceType, CancellationToken cancellationToken)
    {
        await context.ServiceTypes.AddAsync(serviceType, cancellationToken);
    }

    public Task<bool> ExistsByCodeAsync(string normalisedCode, CancellationToken cancellationToken)
    {
        return context.ServiceTypes.AsNoTracking().AnyAsync(s => s.Code == normalisedCode, cancellationToken);
    }

    public Task<ServiceType?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.ServiceTypes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public Task<ServiceType?> GetForUpdateByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.ServiceTypes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public Task<List<ServiceType>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var serviceTypes = context.ServiceTypes.AsNoTracking();
        if (!includeInactive)
        {
            serviceTypes = serviceTypes.Where(s => s.IsActive);
        }

        return serviceTypes.OrderBy(s => s.Name).ThenBy(s => s.Code).ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (UniqueViolation.IsOn(ex, CodeIndexName))
        {
            throw new DuplicateServiceTypeCodeException("A service type with this code already exists.", ex);
        }
    }
}
