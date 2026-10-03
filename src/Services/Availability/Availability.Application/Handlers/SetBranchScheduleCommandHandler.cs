using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class SetBranchScheduleCommandHandler(
    IBranchRepository branchRepository,
    IValidator<SetBranchScheduleCommand> validator,
    ILogger<SetBranchScheduleCommandHandler> logger) : IRequestHandler<SetBranchScheduleCommand, Result<BranchResponse>>
{
    public async Task<Result<BranchResponse>> Handle(SetBranchScheduleCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for setting the schedule of branch {BranchId}: {ValidationErrors}", request.Id, validationErrors);
            return Result<BranchResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var branch = await branchRepository.GetForUpdateByIdAsync(request.Id, cancellationToken);
        if (branch is null)
        {
            logger.LogWarning("Attempt to set the schedule of a branch that does not exist: {BranchId}", request.Id);
            return Result<BranchResponse>.Failure(BranchErrors.NotFound(request.Id));
        }

        // The validator has already checked the format, so the parse cannot fail here.
        var hours = request.WorkingHours.Select(h => new WorkingHours(
            h.DayOfWeek, TimeOnly.ParseExact(h.OpensAt, "HH:mm"), TimeOnly.ParseExact(h.ClosesAt, "HH:mm")));

        branch.SetSchedule(request.TimeZoneId, hours);
        await branchRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Set the schedule of branch {BranchId} to {TimeZoneId} with {DayCount} open days",
            branch.Id, branch.TimeZoneId, branch.WorkingHours.Count);

        return Result<BranchResponse>.Success(BranchResponse.From(branch));
    }
}
