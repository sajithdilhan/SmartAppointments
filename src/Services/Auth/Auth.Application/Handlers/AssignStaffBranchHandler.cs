using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Models;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Enums;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class AssignStaffBranchHandler(
    IUserRepository userRepository,
    IAvailabilityClient availabilityClient,
    IValidator<AssignStaffBranchCommand> validator,
    TimeProvider timeProvider,
    ILogger<AssignStaffBranchHandler> logger) : IRequestHandler<AssignStaffBranchCommand, Result<StaffResponse>>
{
    public async Task<Result<StaffResponse>> Handle(AssignStaffBranchCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for assigning a staff branch: {ValidationErrors}", validationErrors);
            return Result<StaffResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var user = await userRepository.GetForUpdateByIdAsync(request.Id, cancellationToken);
        if (user is null || user.Role != UserRole.Staff)
        {
            return Result<StaffResponse>.Failure(new Error(404, $"Staff member '{request.Id}' was not found."));
        }

        // Moving to the branch they are already in changes nothing, so it must not end their sessions.
        if (user.BranchId == request.BranchId)
        {
            return Result<StaffResponse>.Success(StaffResponse.From(user));
        }

        var branchError = await StaffBranchCheck.CheckAsync(availabilityClient, request.BranchId, logger, cancellationToken);
        if (branchError is not null)
        {
            return Result<StaffResponse>.Failure(branchError);
        }

        user.AssignBranch(request.BranchId);

        // One transaction: the branch change and the session revocation both happen or neither does,
        // so a failure leaves the old branch and an admin retry is not mistaken for the same-branch no-op.
        var revoked = await userRepository.SaveBranchMoveAsync(
            user, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        logger.LogInformation(
            "Moved staff {UserId} to branch {BranchId}; revoked {Revoked} refresh tokens", user.Id, request.BranchId, revoked);

        return Result<StaffResponse>.Success(StaffResponse.From(user));
    }
}
