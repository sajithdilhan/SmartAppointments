using Availability.Application.Abstractions;
using Availability.Application.Commands;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class SetServiceTypeActiveCommandHandler(
    IServiceTypeRepository serviceTypeRepository,
    ILogger<SetServiceTypeActiveCommandHandler> logger) : IRequestHandler<SetServiceTypeActiveCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetServiceTypeActiveCommand request, CancellationToken cancellationToken)
    {
        var serviceType = await serviceTypeRepository.GetForUpdateByIdAsync(request.Id, cancellationToken);
        if (serviceType is null)
        {
            logger.LogWarning("Attempt to change the state of a service type that does not exist: {ServiceTypeId}", request.Id);
            return Result<bool>.Failure(ServiceTypeErrors.NotFound(request.Id));
        }

        var changed = request.IsActive ? serviceType.Activate() : serviceType.Deactivate();

        // Already in the requested state: succeed without a write, so both endpoints are idempotent.
        if (changed)
        {
            await serviceTypeRepository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Set service type {ServiceTypeId} IsActive to {IsActive}", serviceType.Id, serviceType.IsActive);
        }

        return Result<bool>.Success(changed);
    }
}
