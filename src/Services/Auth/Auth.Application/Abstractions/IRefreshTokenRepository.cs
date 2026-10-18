using Auth.Domain.Entities;

namespace Auth.Application.Abstractions;

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Stages a new family. Committed by <see cref="IUserRepository.SaveChangesAsync"/> (same scoped
    /// DbContext), so a login's LastLoginAtUtc and its token are one commit.
    /// </summary>
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a token for query purposes only (no tracking). Null when no row has this hash.
    /// </summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the current token and inserts its successor in one transaction. True when this call
    /// revoked it; false when it was already revoked, in which case nothing was written.
    /// </summary>
    Task<bool> TryRotateAsync(Guid currentTokenId, RefreshToken successor, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every not-yet-revoked token of the family. Idempotent; returns the rows changed.
    /// </summary>
    Task<int> RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken cancellationToken);
}
