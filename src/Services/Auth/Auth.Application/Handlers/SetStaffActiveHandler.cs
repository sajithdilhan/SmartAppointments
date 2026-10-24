using Auth.Application.Abstractions;
using Auth.Application.Commands;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Enums;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class SetStaffActiveHandler(
    IUserRepository userRepository,
    IAvailabilityClient availabilityClient,
    TimeProvider timeProvider,
    ILogger<SetStaffActiveHandler> logger) : IRequestHandler<SetStaffActiveCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(SetStaffActiveCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetForUpdateByIdAsync(request.Id, cancellationToken);
        if (user is null || user.Role != UserRole.Staff || user.IsDeleted)
        {
            return Result<Unit>.Failure(new Error(404, $"Staff member '{request.Id}' was not found."));
        }

        if (!request.IsActive)
        {
            user.Deactivate();

            // Saved and revoked even when already inactive, so a repeat still ends any session a
            // racing login created after the first deactivation.
            var revoked = await userRepository.SaveAndRevokeSessionsAsync(
                user, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

            logger.LogInformation("Deactivated staff {UserId}; revoked {Revoked} refresh tokens", user.Id, revoked);
            return Result<Unit>.Success(Unit.Value);
        }

        if (user.IsActive)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var branchError = await StaffBranchCheck.CheckAsync(availabilityClient, user.BranchId!.Value, logger, cancellationToken);
        if (branchError is not null)
        {
            return Result<Unit>.Failure(branchError);
        }

        // No revocation: the sessions ended when the account was deactivated, and none comes back.
        user.Activate();
        await userRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Activated staff {UserId}", user.Id);
        return Result<Unit>.Success(Unit.Value);
    }
}
