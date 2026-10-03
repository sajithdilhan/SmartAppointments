using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace Availability.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SlotsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Creates back-to-back slots from the branch's working hours for each date in the range.
    /// Candidates that overlap existing slots are skipped and counted.
    /// </summary>
    [HttpPost("generate")]
    [Authorize(Policy = Constants.AdminPolicy)]
    public async Task<IActionResult> Generate(GenerateSlotsRequest request, CancellationToken cancellationToken)
    {
        var command = new GenerateSlotsCommand(
            request.BranchId,
            request.ServiceTypeId,
            request.FromDate,
            request.ToDate,
            request.Capacity);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The slots on a local date that start after now and still have capacity. The BRD names the
    /// service parameter <c>serviceId</c>.
    /// </summary>
    [HttpGet("available")]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> GetAvailable(
        [FromQuery] Guid branchId,
        [FromQuery] Guid serviceId,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SearchAvailableSlotsQuery(branchId, serviceId, date), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }
}
