using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Queries;
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

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// Lists staff accounts (not deleted ones), oldest registration first. Optional <c>branchId</c>,
    /// <c>isActive</c> (true or false), <c>page</c> (default 1) and <c>pageSize</c> (default 20, max 100).
    /// </summary>
    // The four values are bound as strings so that a bad one reaches the validator, which lists every
    // failure in one 400, instead of failing model binding. MVC binds "?isActive=" to null, the same as
    // an omitted parameter, so Raw() restores "" from the query string (an empty value is invalid).
    [HttpGet]
    [ProducesResponseType<StaffPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? branchId,
        [FromQuery] string? isActive,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ListStaffQuery(Raw("branchId", branchId), Raw("isActive", isActive), Raw("page", page), Raw("pageSize", pageSize)),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    /// <summary>Reads one staff account, active or not. Deleted accounts and non-staff users are 404.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetStaffQuery(id), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
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

    /// <summary>
    /// Deactivates a staff member and ends all their refresh-token sessions. Repeating it is safe and
    /// still revokes any live session. Access tokens already issued work until they expire.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        SetActive(id, false, cancellationToken);

    /// <summary>
    /// Reactivates a deactivated staff member whose branch still exists and is active. Their old
    /// sessions stay ended, so they sign in again.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        SetActive(id, true, cancellationToken);

    /// <summary>
    /// Deletes a staff account (soft delete) and ends all their refresh-token sessions. The account
    /// can no longer sign in or be read, and its email stays taken.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteStaffCommand(id), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return NoContent();
    }

    private string? Raw(string name, string? bound) =>
        bound ?? (Request.Query.ContainsKey(name) ? string.Empty : null);

    private async Task<IActionResult> SetActive(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SetStaffActiveCommand(id, isActive), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return NoContent();
    }
}
