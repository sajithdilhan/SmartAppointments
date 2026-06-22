using Auth.Domain.Entities;

namespace Auth.Application.Abstractions;

public interface IUserRepository
{
    Task CreateCustomer(User user, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
}
