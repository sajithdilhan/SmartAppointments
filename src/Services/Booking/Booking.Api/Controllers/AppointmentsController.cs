using Booking.Application.Commands;
using Booking.Application.Models;
using Booking.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace Booking.Api.Controllers;

[Route("api/appointments")]
[ApiController]
public class AppointmentsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Idempotent per <c>Idempotency-Key</c>: a repeat with the same key and body returns the first
    /// outcome (the same status and body, and for a success the same <c>Location</c>) without booking again.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Constants.CustomerPolicy)]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CreateAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out _))
        {
            return Unauthorised();
        }

        var result = await sender.Send(
            new CreateAppointmentCommand(callerId, idempotencyKey, request.SlotId), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        // A replayed 201 takes this same path, and the id comes from the stored body, so the
        // Location is identical to the first response's.
        var appointment = result.Value!;
        return CreatedAtAction(nameof(GetById), new { id = appointment.Id }, appointment);
    }

    /// <summary>
    /// The calling customer's appointments, filtered by <c>status</c> (Booked, Cancelled) and <c>when</c>
    /// (upcoming, past), one page at a time. Upcoming is soonest first; past and unfiltered are most recent first.
    /// <c>page</c> is at least 1 (default 1) and <c>pageSize</c> is 1 to 100 (default 20).
    /// </summary>
    // The four values are bound as strings so that a bad one reaches the validator, which lists every
    // failure in one 400, instead of failing model binding. MVC binds "?status=" to null, the same as
    // an omitted parameter, so Raw() restores "" from the query string (an empty value is invalid).
    [HttpGet("my")]
    [Authorize(Policy = Constants.CustomerPolicy)]
    public async Task<IActionResult> GetMine(
        [FromQuery] string? status,
        [FromQuery] string? when,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out _))
        {
            return Unauthorised();
        }

        var result = await sender.Send(
            new ListMyAppointmentsQuery(
                callerId, Raw("status", status), Raw("when", when), Raw("page", page), Raw("pageSize", pageSize)), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out var role))
        {
            return Unauthorised();
        }

        var result = await sender.Send(new GetAppointmentQuery(id, callerId, role), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Constants.AllowedOriginsPolicy)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var callerId, out var role))
        {
            return Unauthorised();
        }

        var result = await sender.Send(new CancelAppointmentCommand(id, callerId, role), cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        return NoContent();
    }

    private string? Raw(string name, string? bound) =>
        bound ?? (Request.Query.ContainsKey(name) ? string.Empty : null);

    private ObjectResult Unauthorised() => this.ToActionResult(new Error(401, "The token has no valid subject."));

    private bool TryGetCaller(out Guid callerId, out string role)
    {
        role = User.FindFirst(Constants.RoleClaimType)?.Value ?? string.Empty;
        return Guid.TryParse(User.FindFirst(Constants.UserIdClaimType)?.Value, out callerId) && callerId != Guid.Empty;
    }
}
