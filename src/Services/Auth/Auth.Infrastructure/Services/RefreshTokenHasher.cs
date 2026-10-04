using Auth.Application.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace Auth.Infrastructure.Services;

// SHA-256 rather than BCrypt on purpose: the hash is the lookup key, so it must be deterministic,
// and a 512-bit random secret has nothing to brute-force. The string is hashed exactly as presented,
// so a non-canonical spelling of a token simply fails to match.
public class RefreshTokenHasher : IRefreshTokenHasher
{
    public string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
