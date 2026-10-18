using Auth.Application.Commands;
using Auth.Application.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace Auth.Api.Controllers;

/// <summary>
/// Admin-only management of staff accounts and the branch each works at.
/// </summary>
[Route("api/auth/staff")]
[ApiController]
[Authorize(Policy = Constants.AdminPolicy)]
public class StaffController(ISender sender) : ControllerBase
{
    /// <summary>Creates a staff account for a branch that exists and is active.</summary>
    [HttpPost]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateStaffCommand(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PhoneNumber,
            request.Password,
            request.BranchId);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        // No staff GET exists to point a Location header at.
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// Moves a staff member to another branch and ends all their refresh-token sessions, so they sign
    /// in again under the new branch. Access tokens already issued keep the old branch until they expire.
    /// </summary>
    [HttpPut("{id:guid}/branch")]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AssignBranch(Guid id, AssignStaffBranchRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AssignStaffBranchCommand(id, request.BranchId), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }
}
