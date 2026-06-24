using Auth.Application.Abstractions;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

public class UserRepository(ApplicationDbContext context) : IUserRepository
{
    public async Task CreateCustomer(User user, CancellationToken cancellationToken)
    {
        await context.Users.AddAsync(user, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var emailAdd = Email.Create(email);
        return await context.Users.FirstOrDefaultAsync(u => u.Email == emailAdd, cancellationToken);
    }
}
