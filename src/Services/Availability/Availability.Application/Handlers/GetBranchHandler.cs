using Availability.Application.Abstractions;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class GetBranchHandler(IBranchRepository branchRepository) : IRequestHandler<GetBranchQuery, Result<BranchResponse>>
{
    public async Task<Result<BranchResponse>> Handle(GetBranchQuery request, CancellationToken cancellationToken)
    {
        var branch = await branchRepository.GetByIdAsync(request.Id, cancellationToken);

        // A closed branch must look exactly like a missing one to anyone who cannot act on it,
        // so both cases return the same 404 with the same message.
        if (branch is null || (!branch.IsActive && request.CurrentUserRole != Constants.AdminRole))
        {
            return Result<BranchResponse>.Failure(BranchErrors.NotFound(request.Id));
        }

        return Result<BranchResponse>.Success(BranchResponse.From(branch));
    }
}
