using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Availability.Infrastructure.Persistence;

public class BranchRepository(ApplicationDbContext context) : IBranchRepository
{
    public async Task AddAsync(Branch branch, CancellationToken cancellationToken)
    {
        await context.Branches.AddAsync(branch, cancellationToken);
    }

    public Task<bool> ExistsByCodeAsync(string normalisedCode, CancellationToken cancellationToken)
    {
        return context.Branches.AsNoTracking().AnyAsync(b => b.Code == normalisedCode, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Translated here rather than leaking DbUpdateException upwards: the Application layer
            // has no EF Core reference and should not learn one to handle a duplicate code.
            throw new DuplicateBranchCodeException("A branch with this code already exists.", ex);
        }
    }

    // 23505 is the PostgreSQL unique_violation SQLSTATE. Branches has exactly one unique index,
    // IX_Branches_Code, so a unique violation on this context can only be the code.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
