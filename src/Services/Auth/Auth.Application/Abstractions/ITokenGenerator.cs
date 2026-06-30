using Auth.Domain.Entities;
using System.Security.Claims;

namespace Auth.Application.Abstractions;

public interface ITokenGenerator
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}
