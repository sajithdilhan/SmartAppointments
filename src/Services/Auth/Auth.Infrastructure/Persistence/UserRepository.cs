using Auth.Application.Abstractions;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartAppointments.BuildingBlocks.Enums;

namespace Auth.Infrastructure.Persistence;

public class UserRepository(ApplicationDbContext context) : IUserRepository
{
    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        await context.Users.AddAsync(user, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        return await QueryByEmail(context.Users.AsNoTracking(), email, cancellationToken);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetForUpdateByEmailAsync(string email, CancellationToken cancellationToken)
    {
        return await QueryByEmail(context.Users, email, cancellationToken);
    }

    public async Task<User?> GetForUpdateByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        return await context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken);
    }

    public async Task<StaffListPage> ListStaffAsync(
        Guid? branchId, bool? isActive, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Staff && u.DeletedAtUtc == null);
        if (branchId is { } b)
        {
            query = query.Where(u => u.BranchId == b);
        }

        if (isActive is { } active)
        {
            query = query.Where(u => u.IsActive == active);
        }

        var total = await query.CountAsync(cancellationToken);

        // A long, because page can be up to int.MaxValue. Past the end there is nothing to read, and
        // once this passes skip < total, so the int cast below is safe.
        var skip = (long)(page - 1) * pageSize;
        if (skip >= total)
        {
            return new StaffListPage([], total);
        }

        var items = await query.OrderBy(u => u.RegistrationDateUtc).ThenBy(u => u.Id)
            .Skip((int)skip).Take(pageSize).ToListAsync(cancellationToken);
        return new StaffListPage(items, total);
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
            // has no EF Core reference and should not learn one to handle a duplicate address.
            throw new DuplicateEmailException("Email is already registered.", ex);
        }
    }

    public async Task<int> SaveAndRevokeSessionsAsync(User user, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // Same transaction pattern as RefreshTokenRepository.TryRotateAsync: disposing uncommitted rolls
        // back, so a failed revoke leaves the user unchanged and the admin's retry is not a no-op. No
        // retry execution strategy is configured; if one is added this must move inside it.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        var revoked = await context.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, nowUtc), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return revoked;
    }

    // 23505 is the PostgreSQL unique_violation SQLSTATE. The context also holds RefreshTokens, whose
    // unique token-hash index must not be reported as a duplicate email, so the index is named.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Users_Email"
        };

    // Goes through Email.Create so the lookup is normalised the same way the stored value was,
    // rather than comparing against whatever casing or whitespace the caller happened to supply.
    private static Task<User?> QueryByEmail(IQueryable<User> users, string email, CancellationToken cancellationToken)
    {
        var value = Email.Create(email);
        return users.FirstOrDefaultAsync(u => u.Email == value, cancellationToken);
    }
}
