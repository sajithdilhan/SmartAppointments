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
public class BranchesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Lists active branches. <paramref name="includeInactive"/> adds the closed ones, but only
    /// for an admin; anyone else gets the active list whatever they ask for.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> GetBranches([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var query = new GetBranchesQuery(includeInactive, CurrentUserRole);

        var result = await sender.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns one branch. An inactive branch is a 404 for everyone but an admin.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> GetBranch(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetBranchQuery(id, CurrentUserRole), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
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
            return this.ToActionResult(result.Error!);
        }

        return CreatedAtAction(nameof(GetBranch), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Constants.AdminPolicy)]
    public async Task<IActionResult> Update(Guid id, UpdateBranchRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateBranchCommand(
            id,
            request.Name,
            request.Description,
            request.Address,
            request.PhoneNumber);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Constants.AdminPolicy)]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        SetActive(id, isActive: true, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Constants.AdminPolicy)]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        SetActive(id, isActive: false, cancellationToken);

    private async Task<IActionResult> SetActive(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SetBranchActiveCommand(id, isActive), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        // 204 whether or not anything changed: a repeat call is a success, not a conflict.
        return NoContent();
    }

    // Read here and passed into the query, so that handlers never reach into HttpContext.
    private string? CurrentUserRole => User.FindFirst(Constants.RoleClaimType)?.Value;

}
