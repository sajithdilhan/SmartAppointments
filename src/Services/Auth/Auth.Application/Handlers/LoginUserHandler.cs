using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class LoginUserHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ILogger<LoginUserHandler> logger,
    ITokenGenerator tokenGenerator,
    IValidator<LoginUserCommand> validator,
    IRefreshTokenRepository refreshTokenRepository,
    IRefreshTokenHasher refreshTokenHasher,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IRequestHandler<LoginUserCommand, Result<TokenResponse>>
{
    public async Task<Result<TokenResponse>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            logger.LogError("Request is null.");
            return Result<TokenResponse>.Failure(new Error(400, "Request is null."));
        }

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
            logger.LogError("Invalid request: {Errors}", errors);
            return Result<TokenResponse>.Failure(new Error(400, $"Invalid request: {errors}"));
        }

        // Unknown email, inactive account and wrong password all return the same 401 so the
        // endpoint cannot be used to enumerate which addresses are registered.
        var user = await userRepository.GetForUpdateByEmailAsync(request.Email, cancellationToken);
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Login failed for {Email}.", request.Email);
            return Result<TokenResponse>.Failure(new Error(401, "Invalid user or password."));
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            logger.LogWarning("Login failed for {Email}.", request.Email);
            return Result<TokenResponse>.Failure(new Error(401, "Invalid user or password."));
        }

        // Everything that can throw (a misconfigured lifetime) runs before the save, so a bad
        // configuration writes nothing.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = tokenGenerator.GenerateAccessToken(user);
        var refreshToken = tokenGenerator.GenerateRefreshToken();
        var options = jwtOptions.Value;

        // Each login starts its own family, so other sessions of the same user are left alone.
        var family = RefreshToken.StartFamily(
            user.Id,
            refreshTokenHasher.Hash(refreshToken),
            now,
            options.GetRefreshTokenLifetime(),
            options.GetRefreshTokenFamilyMaxLifetime());

        // Both repositories share the scoped DbContext: the login timestamp and the new token commit together.
        user.RecordLogin();
        await refreshTokenRepository.AddAsync(family, cancellationToken);
        await userRepository.SaveChangesAsync(cancellationToken);

        return Result<TokenResponse>.Success(new TokenResponse(token.Value, refreshToken, token.ExpiresAtUtc));
    }
}
