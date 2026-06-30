using Auth.Application.Abstractions;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class GetCustomerByEmailHandler(IUserRepository userRepository,
    ILogger<GetCustomerByEmailHandler> logger) : IRequestHandler<GetCustomerQuery, Result<GetCustomerResponse?>>
{
    public async Task<Result<GetCustomerResponse?>> Handle(GetCustomerQuery request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Customer with email {Email} not found", request.Email);
            return Result<GetCustomerResponse?>.Failure(new Error(404, "Customer not found"));
        }

        var response = new GetCustomerResponse(
            user.FirstName,
            user.LastName,
            user.Email.Value,
            user.PhoneNumber,
            user.IsActive);
        return Result<GetCustomerResponse?>.Success(response);
    }
}
