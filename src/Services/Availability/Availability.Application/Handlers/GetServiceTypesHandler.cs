using Availability.Application.Abstractions;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class GetServiceTypesHandler(IServiceTypeRepository serviceTypeRepository) : IRequestHandler<GetServiceTypesQuery, Result<List<ServiceTypeResponse>>>
{
    public async Task<Result<List<ServiceTypeResponse>>> Handle(GetServiceTypesQuery request, CancellationToken cancellationToken)
    {
        // Only an admin can act on a withdrawn service, so for anyone else includeInactive is
        // ignored rather than rejected.
        var includeInactive = request.IncludeInactive && request.CurrentUserRole == Constants.AdminRole;

        var serviceTypes = await serviceTypeRepository.ListAsync(includeInactive, cancellationToken);

        return Result<List<ServiceTypeResponse>>.Success(serviceTypes.Select(ServiceTypeResponse.From).ToList());
    }
}
