using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Models;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class LoginUserHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ILogger<LoginUserHandler> logger,
    ITokenGenerator tokenGenerator,
    IValidator<LoginUserCommand> validator) : IRequestHandler<LoginUserCommand, Result<TokenResponse>>
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
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);
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

        user.RecordLogin();
        await userRepository.SaveChangesAsync(cancellationToken);

        // Generate and return the token response
        var token = tokenGenerator.GenerateAccessToken(user);
        var refreshToken = tokenGenerator.GenerateRefreshToken();
        return Result<TokenResponse>.Success(new TokenResponse(token, refreshToken));
    }
}
