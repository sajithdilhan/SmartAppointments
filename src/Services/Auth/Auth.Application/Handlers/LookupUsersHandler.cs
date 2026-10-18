using Auth.Application.Abstractions;
using Auth.Application.Models;
using Auth.Application.Queries;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class LookupUsersHandler(
    IUserRepository userRepository,
    IValidator<LookupUsersQuery> validator,
    ILogger<LookupUsersHandler> logger) : IRequestHandler<LookupUsersQuery, Result<IReadOnlyList<UserLookupItem>>>
{
    public async Task<Result<IReadOnlyList<UserLookupItem>>> Handle(LookupUsersQuery request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for looking up users: {ValidationErrors}", validationErrors);
            return Result<IReadOnlyList<UserLookupItem>>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var ids = request.Ids!.Distinct().ToList();
        var users = await userRepository.ListByIdsAsync(ids, cancellationToken);

        // Users of every role are returned; the caller only ever asks for its own customers' ids.
        IReadOnlyList<UserLookupItem> items = users
            .Select(u => new UserLookupItem(u.Id, u.FirstName, u.LastName, u.PhoneNumber))
            .ToList();

        return Result<IReadOnlyList<UserLookupItem>>.Success(items);
    }
}
