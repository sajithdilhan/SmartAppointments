using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class GetBranchesHandler(ILogger<GetBranchesHandler> logger) : IRequestHandler<GetBranchesQuery, Result<List<BranchDto>>>
{
    public async Task<Result<List<BranchDto>>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        // Implement the logic to retrieve branches from the repository or service
        // For example:
        // var branches = await branchRepository.GetAllBranchesAsync(cancellationToken);
        // return Result<List<BranchDto>>.Success(branches);
        // Placeholder implementation
        logger.LogInformation("Handling GetBranchesQuery");
        var b1 = new BranchDto(Guid.CreateVersion7(), "Main Branch", "123 Main St", "555-1234");
        var b2 = new BranchDto(Guid.CreateVersion7(), "Secondary Branch", "456 Elm St", "555-5678");
        var branches = new List<BranchDto>();
        branches.Add(b1);
        branches.Add(b2);

        return Result<List<BranchDto>>.Success(branches);
    }
}
