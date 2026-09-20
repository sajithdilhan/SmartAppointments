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

    // 23505 is the PostgreSQL unique_violation SQLSTATE. Users has exactly one unique index,
    // IX_Users_Email, so a unique violation on this context can only be the email.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // Goes through Email.Create so the lookup is normalised the same way the stored value was,
    // rather than comparing against whatever casing or whitespace the caller happened to supply.
    private static Task<User?> QueryByEmail(IQueryable<User> users, string email, CancellationToken cancellationToken)
    {
        var value = Email.Create(email);
        return users.FirstOrDefaultAsync(u => u.Email == value, cancellationToken);
    }
}
