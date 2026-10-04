using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Commands;

public sealed record LogoutCommand(string? RefreshToken) : IRequest<Result<Unit>>;
