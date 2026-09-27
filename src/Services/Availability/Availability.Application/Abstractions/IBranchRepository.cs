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
    /// Commits staged changes. Throws <see cref="DuplicateBranchCodeException"/> when the unique
    /// index on the code rejects the write.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
