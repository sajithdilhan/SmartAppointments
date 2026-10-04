using Auth.Application.Abstractions;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
