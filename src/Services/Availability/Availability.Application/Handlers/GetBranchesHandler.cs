using Availability.Application.Models;
using Availability.Application.Queries;
using Availability.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class GetBranchesHandler(ILogger<GetBranchesHandler> logger) : IRequestHandler<GetBranchesQuery, Result<List<BranchResponse>>>
{
    public async Task<Result<List<BranchResponse>>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        // Implement the logic to retrieve branches from the repository or service
        // For example:
        // var branches = await branchRepository.GetAllBranchesAsync(cancellationToken);
        // return Result<List<BranchResponse>>.Success(branches);
        // Placeholder implementation
        logger.LogInformation("Handling GetBranchesQuery");
        var b1 = BranchResponse.From(Branch.Create("MAIN", "Main Branch", null, "123 Main St", "+15551234"));
        var b2 = BranchResponse.From(Branch.Create("SEC", "Secondary Branch", null, "456 Elm St", "+15555678"));
        var branches = new List<BranchResponse>();
        branches.Add(b1);
        branches.Add(b2);

        return Result<List<BranchResponse>>.Success(branches);
    }
}
