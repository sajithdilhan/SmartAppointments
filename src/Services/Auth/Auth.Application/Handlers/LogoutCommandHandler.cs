using Auth.Application.Abstractions;
using Auth.Application.Commands;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IRefreshTokenHasher hasher,
    IValidator<LogoutCommand> validator,
    TimeProvider timeProvider,
    ILogger<LogoutCommandHandler> logger) : IRequestHandler<LogoutCommand, Result<Unit>>
{
    // Same bound as refresh: a real token is 88 characters.
    private const int MaxTokenLength = 256;

    public async Task<Result<Unit>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
            logger.LogWarning("Invalid logout request: {Errors}", errors);
            return Result<Unit>.Failure(new Error(400, $"Invalid request: {errors}"));
        }

        // Logout never reveals whether the token existed: empty, over-long and unknown tokens all succeed.
        var presented = request.RefreshToken!;
        if (string.IsNullOrWhiteSpace(presented) || presented.Length > MaxTokenLength)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var stored = await refreshTokens.GetByHashAsync(hasher.Hash(presented), cancellationToken);
        if (stored is null)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        // Whether the presented token is current, rotated, expired or already revoked, the whole family
        // goes. The statement filters on FamilyId, so the user's other logins are left alone.
        var revoked = await refreshTokens.RevokeFamilyAsync(stored.FamilyId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        logger.LogInformation("Logout revoked {Count} token(s) of family {FamilyId}.", revoked, stored.FamilyId);
        return Result<Unit>.Success(Unit.Value);
    }
}
