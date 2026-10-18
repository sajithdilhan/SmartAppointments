using Availability.Application.Abstractions;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

/// <summary>
/// Unlike <see cref="GetBranchHandler"/> this does not hide inactive branches: the caller is
/// another service that decides what to do with <c>IsActive</c>.
/// </summary>
public class GetInternalBranchHandler(IBranchRepository branchRepository)
    : IRequestHandler<GetInternalBranchQuery, Result<InternalBranchResponse>>
{
    public async Task<Result<InternalBranchResponse>> Handle(GetInternalBranchQuery request, CancellationToken cancellationToken)
    {
        var branch = await branchRepository.GetByIdAsync(request.Id, cancellationToken);
        if (branch is null)
        {
            return Result<InternalBranchResponse>.Failure(BranchErrors.NotFound(request.Id));
        }

        return Result<InternalBranchResponse>.Success(new InternalBranchResponse(branch.Id, branch.IsActive, branch.TimeZoneId));
    }
}
