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
/// Service-to-service calls from Booking, authenticated by API key only. Kept apart from
/// <see cref="SlotsController"/> so the public and internal surfaces cannot share an attribute by
/// accident, and so a gateway rule can block <c>/internal/**</c> at the edge.
/// </summary>
[Route("internal/slots")]
[ApiController]
[Authorize(Policy = Constants.InternalServicePolicy)]
public class InternalSlotsController(ISender sender) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetInternalSlotQuery(id), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Idempotent per appointment: repeating a successful reserve changes nothing and succeeds.
    /// </summary>
    [HttpPost("{id:guid}/reserve")]
    public async Task<IActionResult> Reserve(Guid id, SlotReservationRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReserveSlotCommand(id, request.AppointmentId), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return NoContent();
    }

    /// <summary>
    /// Idempotent per appointment: releasing a place that is not held succeeds.
    /// </summary>
    [HttpPost("{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id, SlotReservationRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReleaseSlotCommand(id, request.AppointmentId), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return NoContent();
    }
}
