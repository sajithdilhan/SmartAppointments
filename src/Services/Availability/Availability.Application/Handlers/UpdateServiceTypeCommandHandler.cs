using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Models;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class UpdateServiceTypeCommandHandler(
    IServiceTypeRepository serviceTypeRepository,
    IValidator<UpdateServiceTypeCommand> validator,
    ILogger<UpdateServiceTypeCommandHandler> logger) : IRequestHandler<UpdateServiceTypeCommand, Result<ServiceTypeResponse>>
{
    public async Task<Result<ServiceTypeResponse>> Handle(UpdateServiceTypeCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for updating service type {ServiceTypeId}: {ValidationErrors}", request.Id, validationErrors);
            return Result<ServiceTypeResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var serviceType = await serviceTypeRepository.GetForUpdateByIdAsync(request.Id, cancellationToken);
        if (serviceType is null)
        {
            logger.LogWarning("Attempt to update a service type that does not exist: {ServiceTypeId}", request.Id);
            return Result<ServiceTypeResponse>.Failure(ServiceTypeErrors.NotFound(request.Id));
        }

        serviceType.UpdateDetails(request.Name, request.Description, request.DurationMinutes);
        await serviceTypeRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Updated service type {ServiceTypeId}", serviceType.Id);

        return Result<ServiceTypeResponse>.Success(ServiceTypeResponse.From(serviceType));
    }
}
