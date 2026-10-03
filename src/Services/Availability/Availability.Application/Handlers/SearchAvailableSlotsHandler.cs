using Availability.Application.Abstractions;
using Availability.Application.Models;
using Availability.Application.Queries;
using FluentValidation;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class SearchAvailableSlotsHandler(
    IBranchRepository branchRepository,
    IServiceTypeRepository serviceTypeRepository,
    ISlotRepository slotRepository,
    IValidator<SearchAvailableSlotsQuery> validator,
    TimeProvider timeProvider) : IRequestHandler<SearchAvailableSlotsQuery, Result<List<SlotResponse>>>
{
    public async Task<Result<List<SlotResponse>>> Handle(SearchAvailableSlotsQuery request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            return Result<List<SlotResponse>>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        // Nothing at an inactive branch or for an inactive service can be booked, so for every
        // role it looks exactly like a missing one.
        var branch = await branchRepository.GetByIdAsync(request.BranchId, cancellationToken);
        if (branch is null || !branch.IsActive)
        {
            return Result<List<SlotResponse>>.Failure(BranchErrors.NotFound(request.BranchId));
        }

        var serviceType = await serviceTypeRepository.GetByIdAsync(request.ServiceTypeId, cancellationToken);
        if (serviceType is null || !serviceType.IsActive)
        {
            return Result<List<SlotResponse>>.Failure(ServiceTypeErrors.NotFound(request.ServiceTypeId));
        }

        // Without a schedule no slot can exist.
        if (string.IsNullOrWhiteSpace(branch.TimeZoneId))
        {
            return Result<List<SlotResponse>>.Success([]);
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(branch.TimeZoneId);
        var slots = await slotRepository.SearchAvailableAsync(
            branch.Id,
            serviceType.Id,
            request.Date!.Value,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        return Result<List<SlotResponse>>.Success(slots.Select(s => SlotResponse.From(s, zone)).ToList());
    }
}
