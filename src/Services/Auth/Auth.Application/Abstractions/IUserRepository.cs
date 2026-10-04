using Auth.Domain.Entities;

namespace Auth.Application.Abstractions;

public interface IUserRepository
{
    /// <summary>
    /// Stages a new user. The caller commits with <see cref="SaveChangesAsync"/>, so that a
    /// registration and anything else it must be atomic with share one transaction.
    /// </summary>
    Task AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a user for query purposes only. Changes made to the returned instance are not
    /// persisted — use <see cref="GetForUpdateByEmailAsync"/> when the caller intends to mutate.
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a user by id for query purposes only (no tracking), as the refresh flow needs the
    /// user's current email, role and active flag without ever saving the user.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a user the caller intends to modify. Changes are persisted by <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task<User?> GetForUpdateByEmailAsync(string email, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
