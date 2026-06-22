using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public sealed class RegisterCustomerCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IValidator<RegisterCustomerCommand> validator) : IRequestHandler<RegisterCustomerCommand, Result<RegisterCustomerResponse>>
{
    public async Task<Result<RegisterCustomerResponse>> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            return Result<RegisterCustomerResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var existingUser = await userRepository.GetByEmailAsync(
            request.Email,
            cancellationToken);

        if (existingUser is not null)
        {
            return Result<RegisterCustomerResponse>.Failure(new Error(400, "Email is already registered."));
        }

        var passwordHash = passwordHasher.Hash(request.Password);

        var user = User.RegisterCustomer(
            request.FirstName,
            request.LastName,
            Email.Create(request.Email),
            request.PhoneNumber,
            passwordHash);

        await userRepository.CreateCustomer(user, cancellationToken);

        return Result<RegisterCustomerResponse>.Success(new RegisterCustomerResponse(
            user.Id,
            user.Email.Value,
            nameof(user.Role)));
    }
}
