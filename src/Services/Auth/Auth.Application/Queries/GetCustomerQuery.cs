using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Queries;

public sealed record GetCustomerQuery(string Email) : IRequest<Result<GetCustomerResponse?>>;

