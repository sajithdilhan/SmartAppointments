using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Models;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class UpdateBranchCommandHandler(
    IBranchRepository branchRepository,
    IValidator<UpdateBranchCommand> validator,
    ILogger<UpdateBranchCommandHandler> logger) : IRequestHandler<UpdateBranchCommand, Result<BranchResponse>>
{
    public async Task<Result<BranchResponse>> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for updating branch {BranchId}: {ValidationErrors}", request.Id, validationErrors);
            return Result<BranchResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var branch = await branchRepository.GetForUpdateByIdAsync(request.Id, cancellationToken);
        if (branch is null)
        {
            logger.LogWarning("Attempt to update a branch that does not exist: {BranchId}", request.Id);
            return Result<BranchResponse>.Failure(BranchErrors.NotFound(request.Id));
        }

        branch.UpdateDetails(request.Name, request.Description, request.Address, request.PhoneNumber);
        await branchRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Updated branch {BranchId}", branch.Id);

        return Result<BranchResponse>.Success(BranchResponse.From(branch));
    }
}
