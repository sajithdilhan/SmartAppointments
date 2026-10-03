using Availability.Application.Abstractions;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class GetServiceTypeHandler(IServiceTypeRepository serviceTypeRepository) : IRequestHandler<GetServiceTypeQuery, Result<ServiceTypeResponse>>
{
    public async Task<Result<ServiceTypeResponse>> Handle(GetServiceTypeQuery request, CancellationToken cancellationToken)
    {
        var serviceType = await serviceTypeRepository.GetByIdAsync(request.Id, cancellationToken);

        // A withdrawn service must look exactly like a missing one to anyone who cannot act on it.
        if (serviceType is null || (!serviceType.IsActive && request.CurrentUserRole != Constants.AdminRole))
        {
            return Result<ServiceTypeResponse>.Failure(ServiceTypeErrors.NotFound(request.Id));
        }

        return Result<ServiceTypeResponse>.Success(ServiceTypeResponse.From(serviceType));
    }
}
