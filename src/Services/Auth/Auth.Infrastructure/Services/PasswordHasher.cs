using Auth.Application.Abstractions;

namespace Auth.Infrastructure.Services;

public class PasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        throw new NotImplementedException();
    }
}
