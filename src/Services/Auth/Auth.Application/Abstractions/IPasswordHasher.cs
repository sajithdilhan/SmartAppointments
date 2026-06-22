namespace Auth.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
}
