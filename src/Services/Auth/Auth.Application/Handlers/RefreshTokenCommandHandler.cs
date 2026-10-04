using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Models;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository userRepository,
    ITokenGenerator tokenGenerator,
    IRefreshTokenHasher hasher,
    IValidator<RefreshTokenCommand> validator,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    ILogger<RefreshTokenCommandHandler> logger) : IRequestHandler<RefreshTokenCommand, Result<TokenResponse>>
{
    // A real token is 88 characters; the cap bounds the work an anonymous caller can force.
    private const int MaxTokenLength = 256;

    // Every 401 carries this one message, so the endpoint cannot be used to discover which tokens
    // exist or why one failed. The reason goes to the server log only, and never the token or its hash.
    private static readonly Error Unauthorized = new(401, "Invalid or expired refresh token.");

    public async Task<Result<TokenResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
            logger.LogWarning("Invalid refresh request: {Errors}", errors);
            return Result<TokenResponse>.Failure(new Error(400, $"Invalid request: {errors}"));
        }

        var presented = request.RefreshToken!;
        if (presented.Length > MaxTokenLength)
        {
            logger.LogWarning("Refresh rejected: token longer than {MaxLength} characters.", MaxTokenLength);
            return Result<TokenResponse>.Failure(Unauthorized);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var stored = await refreshTokens.GetByHashAsync(hasher.Hash(presented), cancellationToken);
        if (stored is null)
        {
            logger.LogWarning("Refresh rejected: unknown token.");
            return Result<TokenResponse>.Failure(Unauthorized);
        }

        // Revoked is checked before expired, so a revoked token always kills its family; an
        // expired-only token does not (expiry is not evidence of a leak).
        if (stored.IsRevoked)
        {
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            logger.LogWarning("Refresh rejected: revoked token reused, family {FamilyId} revoked.", stored.FamilyId);
            return Result<TokenResponse>.Failure(Unauthorized);
        }

        if (stored.IsExpired(now))
        {
            logger.LogWarning("Refresh rejected: token expired, family {FamilyId}.", stored.FamilyId);
            return Result<TokenResponse>.Failure(Unauthorized);
        }

        var user = await userRepository.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            // Revoked too, so that reactivating the account does not resurrect an old session.
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            logger.LogWarning("Refresh rejected: user inactive or gone, family {FamilyId} revoked.", stored.FamilyId);
            return Result<TokenResponse>.Failure(Unauthorized);
        }

        var options = jwtOptions.Value;
        var successorToken = tokenGenerator.GenerateRefreshToken();
        var successor = stored.CreateSuccessor(
            hasher.Hash(successorToken),
            now,
            options.GetRefreshTokenLifetime(),
            options.GetRefreshTokenFamilyMaxLifetime());
        if (successor is null)
        {
            logger.LogWarning("Refresh rejected: family {FamilyId} reached its absolute cap.", stored.FamilyId);
            return Result<TokenResponse>.Failure(Unauthorized);
        }

        // Built from the user as just read, so email and role are current. It runs before the
        // rotation so that a generation failure leaves the presented token unredeemed.
        var access = tokenGenerator.GenerateAccessToken(user);

        var rotated = await refreshTokens.TryRotateAsync(stored.Id, successor, now, cancellationToken);
        if (!rotated)
        {
            // Lost the race, or the token was revoked between the read and the update: a reuse.
            // Runs after the winner's commit, so the winner's successor is revoked too.
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            logger.LogWarning("Refresh rejected: token already redeemed, family {FamilyId} revoked.", stored.FamilyId);
            return Result<TokenResponse>.Failure(Unauthorized);
        }

        // RecordLogin is deliberately not called: a refresh is not a login.
        return Result<TokenResponse>.Success(new TokenResponse(access.Value, successorToken, access.ExpiresAtUtc));
    }
}
