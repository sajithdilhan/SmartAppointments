using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Commands;

public sealed record RegisterCustomerCommand(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Password) : IRequest<Result<RegisterCustomerResponse>>;
