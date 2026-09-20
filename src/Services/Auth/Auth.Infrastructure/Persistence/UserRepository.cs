using Auth.Application.Abstractions;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

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
        await context.SaveChangesAsync(cancellationToken);
    }

    // Goes through Email.Create so the lookup is normalised the same way the stored value was,
    // rather than comparing against whatever casing or whitespace the caller happened to supply.
    private static Task<User?> QueryByEmail(IQueryable<User> users, string email, CancellationToken cancellationToken)
    {
        var value = Email.Create(email);
        return users.FirstOrDefaultAsync(u => u.Email == value, cancellationToken);
    }
}
