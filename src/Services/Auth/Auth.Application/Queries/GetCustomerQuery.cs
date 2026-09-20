using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Queries;

public sealed record GetCustomerQuery(string RequestedEmail, string? CurrentUserEmail, string? CurrentUserRole) : IRequest<Result<GetCustomerResponse?>>;

