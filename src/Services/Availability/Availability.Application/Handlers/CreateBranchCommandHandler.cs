using Availability.Application.Abstractions;
using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class CreateBranchCommandHandler(
    IBranchRepository branchRepository,
    IValidator<CreateBranchCommand> validator,
    ILogger<CreateBranchCommandHandler> logger) : IRequestHandler<CreateBranchCommand, Result<BranchResponse>>
{
    public async Task<Result<BranchResponse>> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for creating branch: {ValidationErrors}", validationErrors);
            return Result<BranchResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var code = Branch.NormaliseCode(request.Code);

        if (await branchRepository.ExistsByCodeAsync(code, cancellationToken))
        {
            logger.LogWarning("Attempt to create a branch with existing code: {Code}", code);
            return Result<BranchResponse>.Failure(DuplicateCode(code));
        }

        var branch = Branch.Create(
            request.Code,
            request.Name,
            request.Description,
            request.Address,
            request.PhoneNumber);

        await branchRepository.AddAsync(branch, cancellationToken);

        try
        {
            await branchRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateBranchCodeException)
        {
            // Two concurrent creates for the same code both pass the check above; the unique
            // index rejects the second. The caller gets the same 409 either way.
            logger.LogWarning("Concurrent branch create lost the race for code: {Code}", code);
            return Result<BranchResponse>.Failure(DuplicateCode(code));
        }

        logger.LogInformation("Created branch {BranchId} with code {Code}", branch.Id, branch.Code);

        return Result<BranchResponse>.Success(BranchResponse.From(branch));
    }

    private static Error DuplicateCode(string code) =>
        new(409, $"A branch with code '{code}' already exists.");
}
