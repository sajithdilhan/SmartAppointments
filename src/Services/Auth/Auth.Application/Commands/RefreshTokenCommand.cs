using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Commands;

public sealed record RefreshTokenCommand(string? RefreshToken) : IRequest<Result<TokenResponse>>;
