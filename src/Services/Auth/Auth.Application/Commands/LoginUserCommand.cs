using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Commands;

public sealed record LoginUserCommand(string Email, string Password) : IRequest<Result<TokenResponse>>;
