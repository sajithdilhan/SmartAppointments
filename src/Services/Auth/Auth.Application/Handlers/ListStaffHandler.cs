using Auth.Application.Abstractions;
using Auth.Application.Models;
using Auth.Application.Queries;
using FluentValidation;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class ListStaffHandler(IUserRepository users, IValidator<ListStaffQuery> validator)
    : IRequestHandler<ListStaffQuery, Result<StaffPage>>
{
    public async Task<Result<StaffPage>> Handle(ListStaffQuery request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage));
            return Result<StaffPage>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        // The validator accepted these, so the parses cannot fail; defaults apply to omitted values.
        StaffListParameters.TryParseBranchId(request.BranchId, out var branchId);
        StaffListParameters.TryParseIsActive(request.IsActive, out var isActive);
        StaffListParameters.TryParsePage(request.Page, out var page);
        StaffListParameters.TryParsePageSize(request.PageSize, out var pageSize);

        var result = await users.ListStaffAsync(branchId, isActive, page, pageSize, cancellationToken);

        return Result<StaffPage>.Success(new StaffPage(
            result.Items.Select(StaffResponse.From).ToList(), page, pageSize, result.TotalCount));
    }
}
