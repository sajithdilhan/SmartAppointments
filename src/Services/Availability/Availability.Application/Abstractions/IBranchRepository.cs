using Availability.Domain.Entities;

namespace Availability.Application.Abstractions;

public interface IBranchRepository
{
    /// <summary>
    /// Stages a new branch. The caller commits with <see cref="SaveChangesAsync"/>, so that a
    /// branch and anything else it must be atomic with share one transaction.
    /// </summary>
    Task AddAsync(Branch branch, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only existence check. <paramref name="normalisedCode"/> must already have been through
    /// <see cref="Branch.NormaliseCode"/>; the comparison is exact.
    /// </summary>
    Task<bool> ExistsByCodeAsync(string normalisedCode, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only lookup, active or inactive. Deciding whether the caller may see an inactive
    /// branch is the handler's job.
    /// </summary>
    Task<Branch?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Tracked lookup for a branch the caller is about to change and commit with
    /// <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task<Branch?> GetForUpdateByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only list ordered by name, then code. Inactive branches are included only when
    /// <paramref name="includeInactive"/> is true.
    /// </summary>
    Task<List<Branch>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    /// <summary>
    /// Commits staged changes. Throws <see cref="DuplicateBranchCodeException"/> when the unique
    /// index on the code rejects the write.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
