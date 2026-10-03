using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Commands;

public sealed record ReleaseSlotCommand(Guid SlotId, Guid AppointmentId) : IRequest<Result<bool>>;
