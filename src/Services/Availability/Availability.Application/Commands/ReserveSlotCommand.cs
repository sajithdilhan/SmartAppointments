using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Commands;

/// <summary>
/// The success value carries no information (always <c>true</c>); the result is the outcome.
/// </summary>
public sealed record ReserveSlotCommand(Guid SlotId, Guid AppointmentId) : IRequest<Result<bool>>;
