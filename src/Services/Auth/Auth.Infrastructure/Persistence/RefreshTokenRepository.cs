using Auth.Application.Abstractions;
using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

public class RefreshTokenRepository(ApplicationDbContext context) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        await context.RefreshTokens.AddAsync(token, cancellationToken);
    }

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        return await context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<bool> TryRotateAsync(
        Guid currentTokenId,
        RefreshToken successor,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // The conditional update runs first and is the only arbiter of who won: under READ COMMITTED a
        // second request on the same row waits for the winner's commit, re-evaluates RevokedAtUtc IS NULL
        // and affects zero rows. Disposing the transaction uncommitted rolls everything back, so a failed
        // insert leaves the presented token unredeemed. No retry execution strategy is configured; if one
        // is ever added this block must move inside CreateExecutionStrategy().ExecuteAsync.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var rows = await context.RefreshTokens
            .Where(t => t.Id == currentTokenId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtUtc, nowUtc)
                .SetProperty(t => t.ReplacedById, successor.Id), cancellationToken);
        if (rows != 1)
        {
            return false;
        }

        context.RefreshTokens.Add(successor);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(successor).State = EntityState.Detached;
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        return await context.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, nowUtc), cancellationToken);
    }
}
