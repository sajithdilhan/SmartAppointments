using Availability.Domain.Entities;

namespace Availability.Application.Abstractions;

/// <summary>
/// Shaped like <see cref="IBranchRepository"/>: staging and committing are separate, reads are
/// no-tracking, and only <see cref="GetForUpdateByIdAsync"/> returns a tracked entity.
/// </summary>
public interface IServiceTypeRepository
{
    Task AddAsync(ServiceType serviceType, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="normalisedCode"/> must already have been through
    /// <see cref="ServiceType.NormaliseCode"/>; the comparison is exact.
    /// </summary>
    Task<bool> ExistsByCodeAsync(string normalisedCode, CancellationToken cancellationToken);

    Task<ServiceType?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ServiceType?> GetForUpdateByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Ordered by name, then code. Inactive service types only when <paramref name="includeInactive"/>.
    /// </summary>
    Task<List<ServiceType>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    /// <summary>
    /// Throws <see cref="DuplicateServiceTypeCodeException"/> when the unique index on the code
    /// rejects the write.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
