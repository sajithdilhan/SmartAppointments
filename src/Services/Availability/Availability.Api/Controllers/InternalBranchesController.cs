using Availability.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace Availability.Api.Controllers;

/// <summary>
/// Service-to-service branch lookup, authenticated by API key only. Kept apart from
/// <see cref="BranchesController"/> so the public and internal surfaces cannot share an attribute.
/// </summary>
[Route("internal/branches")]
[ApiController]
[Authorize(Policy = Constants.InternalServicePolicy)]
public class InternalBranchesController(ISender sender) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetInternalBranchQuery(id), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }
}
