using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Models;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Enums;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class LoginCustomerHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ILogger<LoginCustomerHandler> logger,
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

        var customer = await userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (customer is not { Role: UserRole.Customer })
        {
            logger.LogError("Customer not found.");
            return Result<TokenResponse>.Failure(new Error(404, "Customer not found."));
        }

        if (!passwordHasher.Verify(request.Password, customer.PasswordHash))
        {
            logger.LogError("Invalid user or password.");
            return Result<TokenResponse>.Failure(new Error(401, "Invalid user or password."));
        }

        // Generate and return the token response
        var token = tokenGenerator.GenerateAccessToken(customer);
        var refreshToken = tokenGenerator.GenerateRefreshToken();
        return Result<TokenResponse>.Success(new TokenResponse(token, refreshToken));
    }
}
