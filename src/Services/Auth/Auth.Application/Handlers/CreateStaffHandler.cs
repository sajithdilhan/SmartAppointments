using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class CreateStaffHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAvailabilityClient availabilityClient,
    IValidator<CreateStaffCommand> validator,
    ILogger<CreateStaffHandler> logger) : IRequestHandler<CreateStaffCommand, Result<StaffResponse>>
{
    private const string DuplicateEmailMessage = "Email is already registered.";

    public async Task<Result<StaffResponse>> Handle(CreateStaffCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for creating staff: {ValidationErrors}", validationErrors);
            return Result<StaffResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        // Validation and the email check come before the Availability call, so a bad request never
        // waits on another service.
        var existingUser = await userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existingUser is not null)
        {
            logger.LogWarning("Attempt to create staff with an email that is already registered: {Email}", request.Email);
            return Result<StaffResponse>.Failure(new Error(400, DuplicateEmailMessage));
        }

        var branchError = await StaffBranchCheck.CheckAsync(availabilityClient, request.BranchId, logger, cancellationToken);
        if (branchError is not null)
        {
            return Result<StaffResponse>.Failure(branchError);
        }

        var user = User.RegisterStaff(
            request.FirstName,
            request.LastName,
            Email.Create(request.Email),
            request.PhoneNumber,
            passwordHasher.Hash(request.Password),
            request.BranchId);

        await userRepository.AddAsync(user, cancellationToken);

        try
        {
            await userRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateEmailException)
        {
            // Two concurrent creations for the same address both pass the check above; the unique
            // index rejects the second. The caller gets the same 400 either way.
            logger.LogWarning("Concurrent staff creation lost the race for email: {Email}", request.Email);
            return Result<StaffResponse>.Failure(new Error(400, DuplicateEmailMessage));
        }

        logger.LogInformation("Created staff {UserId} for branch {BranchId}", user.Id, request.BranchId);

        return Result<StaffResponse>.Success(StaffResponse.From(user));
    }
}
