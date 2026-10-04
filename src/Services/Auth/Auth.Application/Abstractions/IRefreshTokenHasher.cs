namespace Auth.Application.Abstractions;

public interface IRefreshTokenHasher
{
    /// <summary>
    /// A deterministic hash of the token, used as its lookup key: the raw token is never stored.
    /// </summary>
    string Hash(string token);
}
