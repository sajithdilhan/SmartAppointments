using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class GenerateSlotsCommandHandler(
    IBranchRepository branchRepository,
    IServiceTypeRepository serviceTypeRepository,
    ISlotRepository slotRepository,
    IValidator<GenerateSlotsCommand> validator,
    TimeProvider timeProvider,
    ILogger<GenerateSlotsCommandHandler> logger) : IRequestHandler<GenerateSlotsCommand, Result<GenerateSlotsResponse>>
{
    public async Task<Result<GenerateSlotsResponse>> Handle(GenerateSlotsCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for generating slots: {ValidationErrors}", validationErrors);
            return Result<GenerateSlotsResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var branch = await branchRepository.GetByIdAsync(request.BranchId, cancellationToken);
        if (branch is null)
        {
            logger.LogWarning("Attempt to generate slots for a branch that does not exist: {BranchId}", request.BranchId);
            return Result<GenerateSlotsResponse>.Failure(BranchErrors.NotFound(request.BranchId));
        }

        var serviceType = await serviceTypeRepository.GetByIdAsync(request.ServiceTypeId, cancellationToken);
        if (serviceType is null)
        {
            logger.LogWarning("Attempt to generate slots for a service type that does not exist: {ServiceTypeId}", request.ServiceTypeId);
            return Result<GenerateSlotsResponse>.Failure(ServiceTypeErrors.NotFound(request.ServiceTypeId));
        }

        if (!branch.IsActive)
        {
            return Result<GenerateSlotsResponse>.Failure(SlotErrors.BranchInactive(branch.Code));
        }

        if (!serviceType.IsActive)
        {
            return Result<GenerateSlotsResponse>.Failure(SlotErrors.ServiceTypeInactive(serviceType.Code));
        }

        if (string.IsNullOrWhiteSpace(branch.TimeZoneId) || branch.WorkingHours.Count == 0)
        {
            return Result<GenerateSlotsResponse>.Failure(SlotErrors.NoWorkingHours(branch.Code));
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var candidates = SlotPlanner.Plan(
            branch, serviceType, request.FromDate, request.ToDate, request.Capacity, nowUtc);

        if (candidates.Count == 0)
        {
            return Result<GenerateSlotsResponse>.Success(new GenerateSlotsResponse(0, 0));
        }

        var existing = await slotRepository.ListOverlappingAsync(
            branch.Id,
            serviceType.Id,
            candidates.Min(c => c.StartUtc),
            candidates.Max(c => c.EndUtc),
            cancellationToken);

        var toCreate = candidates
            .Where(c => !existing.Any(e => c.StartUtc < e.EndUtc && e.StartUtc < c.EndUtc))
            .ToList();
        var skipped = candidates.Count - toCreate.Count;

        if (toCreate.Count > 0)
        {
            try
            {
                await slotRepository.AddRangeAsync(toCreate, cancellationToken);
                await slotRepository.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateSlotException ex)
            {
                // The overlap check cannot see a concurrent generation that has not committed
                // yet; the unique index caught it.
                logger.LogWarning(ex, "Concurrent slot generation for branch {BranchId} and service type {ServiceTypeId}", branch.Id, serviceType.Id);
                return Result<GenerateSlotsResponse>.Failure(SlotErrors.ConcurrentGeneration());
            }
        }

        logger.LogInformation(
            "Generated {CreatedCount} slots for branch {BranchId} and service type {ServiceTypeId}, skipped {SkippedCount}",
            toCreate.Count, branch.Id, serviceType.Id, skipped);

        return Result<GenerateSlotsResponse>.Success(new GenerateSlotsResponse(toCreate.Count, skipped));
    }
}
