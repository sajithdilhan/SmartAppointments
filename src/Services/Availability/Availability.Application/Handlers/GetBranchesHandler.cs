using Availability.Application.Abstractions;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class GetBranchesHandler(IBranchRepository branchRepository) : IRequestHandler<GetBranchesQuery, Result<List<BranchResponse>>>
{
    public async Task<Result<List<BranchResponse>>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        // Only an admin can act on a closed branch, so for anyone else includeInactive is ignored
        // rather than rejected.
        var includeInactive = request.IncludeInactive && request.CurrentUserRole == Constants.AdminRole;

        var branches = await branchRepository.ListAsync(includeInactive, cancellationToken);

        return Result<List<BranchResponse>>.Success(branches.Select(BranchResponse.From).ToList());
    }
}
