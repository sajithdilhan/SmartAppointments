using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class CreateServiceTypeCommandHandler(
    IServiceTypeRepository serviceTypeRepository,
    IValidator<CreateServiceTypeCommand> validator,
    ILogger<CreateServiceTypeCommandHandler> logger) : IRequestHandler<CreateServiceTypeCommand, Result<ServiceTypeResponse>>
{
    public async Task<Result<ServiceTypeResponse>> Handle(CreateServiceTypeCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for creating service type: {ValidationErrors}", validationErrors);
            return Result<ServiceTypeResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var code = ServiceType.NormaliseCode(request.Code);

        if (await serviceTypeRepository.ExistsByCodeAsync(code, cancellationToken))
        {
            logger.LogWarning("Attempt to create a service type with existing code: {Code}", code);
            return Result<ServiceTypeResponse>.Failure(ServiceTypeErrors.DuplicateCode(code));
        }

        var serviceType = ServiceType.Create(request.Code, request.Name, request.Description, request.DurationMinutes);

        await serviceTypeRepository.AddAsync(serviceType, cancellationToken);

        try
        {
            await serviceTypeRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateServiceTypeCodeException)
        {
            // A concurrent create passed the same pre-check; the unique index rejected this one.
            logger.LogWarning("Concurrent service type create lost the race for code: {Code}", code);
            return Result<ServiceTypeResponse>.Failure(ServiceTypeErrors.DuplicateCode(code));
        }

        logger.LogInformation("Created service type {ServiceTypeId} with code {Code}", serviceType.Id, serviceType.Code);

        return Result<ServiceTypeResponse>.Success(ServiceTypeResponse.From(serviceType));
    }
}
