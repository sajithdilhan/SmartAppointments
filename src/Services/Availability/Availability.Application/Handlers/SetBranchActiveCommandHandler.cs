using Availability.Application.Abstractions;
using Availability.Application.Commands;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class SetBranchActiveCommandHandler(
    IBranchRepository branchRepository,
    ILogger<SetBranchActiveCommandHandler> logger) : IRequestHandler<SetBranchActiveCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetBranchActiveCommand request, CancellationToken cancellationToken)
    {
        var branch = await branchRepository.GetForUpdateByIdAsync(request.Id, cancellationToken);
        if (branch is null)
        {
            logger.LogWarning("Attempt to change the state of a branch that does not exist: {BranchId}", request.Id);
            return Result<bool>.Failure(BranchErrors.NotFound(request.Id));
        }

        var changed = request.IsActive ? branch.Activate() : branch.Deactivate();

        // Already in the requested state: succeed without a write, so both endpoints are idempotent.
        if (changed)
        {
            await branchRepository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Set branch {BranchId} IsActive to {IsActive}", branch.Id, branch.IsActive);
        }

        return Result<bool>.Success(changed);
    }
}
