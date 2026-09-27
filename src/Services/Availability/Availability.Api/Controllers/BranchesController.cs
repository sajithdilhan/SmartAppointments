using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BranchesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetBranches(CancellationToken cancellationToken)
    {
        var query = new GetBranchesQuery();
        var result = await sender.Send(query, cancellationToken);
        if (!result.IsSuccess)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    [Authorize(Policy = Constants.AdminPolicy)]
    public async Task<IActionResult> Create(CreateBranchRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateBranchCommand(
            request.Code,
            request.Name,
            request.Description,
            request.Address,
            request.PhoneNumber);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return ToErrorResult(result.Error!);
        }

        // A literal path until GET /api/branches/{id} exists; then this becomes CreatedAtAction.
        return Created($"/api/branches/{result.Value!.Id}", result.Value);
    }

    // Every failure path goes through here so the status a handler chose is the status the caller
    // sees — the same mapping as AuthController.
    private ObjectResult ToErrorResult(Error error) => error.Status switch
    {
        StatusCodes.Status400BadRequest => BadRequest(error),
        StatusCodes.Status401Unauthorized => Unauthorized(error),
        StatusCodes.Status403Forbidden => StatusCode(StatusCodes.Status403Forbidden, error),
        StatusCodes.Status404NotFound => NotFound(error),
        StatusCodes.Status409Conflict => Conflict(error),
        _ => StatusCode(StatusCodes.Status500InternalServerError, error)
    };
}
