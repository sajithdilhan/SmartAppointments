using Booking.Application.Abstractions;
using Booking.Application.Models;
using Booking.Application.Queries;
using FluentValidation;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Handlers;

public class ListMyAppointmentsHandler(
    IAppointmentRepository appointments,
    IValidator<ListMyAppointmentsQuery> validator,
    TimeProvider timeProvider)
    : IRequestHandler<ListMyAppointmentsQuery, Result<PagedResponse<AppointmentResponse>>>
{
    public async Task<Result<PagedResponse<AppointmentResponse>>> Handle(
        ListMyAppointmentsQuery request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage));
            return Result<PagedResponse<AppointmentResponse>>.Failure(
                new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        // The validator accepted these, so the parses cannot fail; defaults apply to omitted values.
        MyAppointmentsParameters.TryParseStatus(request.Status, out var status);
        MyAppointmentsParameters.TryParseWhen(request.When, out var when);
        MyAppointmentsParameters.TryParsePage(request.Page, out var page);
        MyAppointmentsParameters.TryParsePageSize(request.PageSize, out var pageSize);

        // Read once, so the count and the page see the same instant.
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var result = await appointments.ListForCustomerAsync(
            request.CallerId, status, when, nowUtc, page, pageSize, cancellationToken);

        return Result<PagedResponse<AppointmentResponse>>.Success(new PagedResponse<AppointmentResponse>(
            result.Items.Select(AppointmentResponse.From).ToList(), page, pageSize, result.TotalCount));
    }
}
