using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace Availability.Api.Controllers;

/// <summary>
/// The service-type catalogue. The route is /api/services as drafted in the BRD; the type is
/// called ServiceType to avoid the overloaded word "service".
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class ServicesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Lists active service types. <paramref name="includeInactive"/> adds withdrawn ones, but
    /// only for an admin.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> GetServiceTypes([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetServiceTypesQuery(includeInactive, CurrentUserRole), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns one service type. A withdrawn one is a 404 for everyone but an admin.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> GetServiceType(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetServiceTypeQuery(id, CurrentUserRole), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    [Authorize(Policy = Constants.AdminPolicy)]
    public async Task<IActionResult> Create(CreateServiceTypeRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateServiceTypeCommand(
            request.Code,
            request.Name,
            request.Description,
            request.DurationMinutes);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return CreatedAtAction(nameof(GetServiceType), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Constants.AdminPolicy)]
    public async Task<IActionResult> Update(Guid id, UpdateServiceTypeRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateServiceTypeCommand(id, request.Name, request.Description, request.DurationMinutes);

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
        var result = await sender.Send(new SetServiceTypeActiveCommand(id, isActive), cancellationToken);

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
