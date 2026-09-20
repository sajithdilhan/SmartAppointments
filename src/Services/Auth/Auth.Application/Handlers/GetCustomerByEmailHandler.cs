using Auth.Application.Abstractions;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Enums;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class GetCustomerByEmailHandler(IUserRepository userRepository,
    ILogger<GetCustomerByEmailHandler> logger) : IRequestHandler<GetCustomerQuery, Result<GetCustomerResponse?>>
{
    public async Task<Result<GetCustomerResponse?>> Handle(GetCustomerQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentUserEmail) || string.IsNullOrWhiteSpace(request.CurrentUserRole))
            return Result<GetCustomerResponse?>.Failure(new Error(403, "Permission denied."));

        var requestedEmail = request.RequestedEmail;
        if (request.CurrentUserRole == UserRole.Customer.ToString())
        {
            requestedEmail = request.CurrentUserEmail;
        }

        // The repository normalises through Email.Create, which throws on a blank or '@'-less
        // value. Rejecting it here keeps a bad query string a 400 instead of a 500.
        if (string.IsNullOrWhiteSpace(requestedEmail) || !requestedEmail.Contains('@'))
        {
            logger.LogWarning("Profile requested with a malformed email value.");
            return Result<GetCustomerResponse?>.Failure(new Error(400, "A valid email is required."));
        }

        var user = await userRepository.GetByEmailAsync(requestedEmail, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Customer with email {Email} not found", request.RequestedEmail);
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
