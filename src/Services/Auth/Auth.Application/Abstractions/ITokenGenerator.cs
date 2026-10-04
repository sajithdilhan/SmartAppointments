using Auth.Application.Models;
using Auth.Domain.Entities;

namespace Auth.Application.Abstractions;

public interface ITokenGenerator
{
    AccessToken GenerateAccessToken(User user);
    string GenerateRefreshToken();
}
