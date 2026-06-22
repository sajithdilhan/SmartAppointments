using Auth.Application.Abstractions;
using Auth.Domain.Entities;

namespace Auth.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    public Task CreateCustomer(User user, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
