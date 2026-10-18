using Booking.Application.Abstractions;
using Booking.Application.Models;
using Booking.Application.Queries;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Handlers;

public class ListBranchAppointmentsHandler(
    IAppointmentRepository appointments,
    IBranchTimeZoneProvider zoneProvider,
    IAuthClient authClient,
    IValidator<ListBranchAppointmentsQuery> validator,
    TimeProvider timeProvider,
    ILogger<ListBranchAppointmentsHandler> logger)
    : IRequestHandler<ListBranchAppointmentsQuery, Result<BranchAppointmentsPage>>
{
    public async Task<Result<BranchAppointmentsPage>> Handle(
        ListBranchAppointmentsQuery request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage));
            return Result<BranchAppointmentsPage>.Failure(
                new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        // A staff member sees only their own branch; a missing claim (null) matches nothing.
        if (request.CallerRole == Constants.StaffRole && request.CallerBranchId != request.BranchId)
        {
            return Result<BranchAppointmentsPage>.Failure(
                new Error(403, "You can only view your own branch's appointments."));
        }

        // The validator accepted these, so the parses cannot fail; defaults apply to omitted values.
        BranchAppointmentsParameters.TryParseDate(request.Date, out var date);
        BranchAppointmentsParameters.TryParseStatus(request.Status, out var status);
        BranchAppointmentsParameters.TryParsePage(request.Page, out var page);
        BranchAppointmentsParameters.TryParsePageSize(request.PageSize, out var pageSize);

        // Read once, so the default day and everything after it see the same instant.
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var zone = await zoneProvider.GetAsync(request.BranchId, cancellationToken);
        if (!zone.IsSuccess)
        {
            return Result<BranchAppointmentsPage>.Failure(zone.Error!);
        }

        // An unknown zone id still falls back to UTC (a null one is silent), but is worth a warning.
        if (!BranchDay.TryResolve(zone.Value, out _))
        {
            logger.LogWarning(
                "Branch {BranchId} has time zone {TimeZoneId}, which this runtime does not know; using UTC.",
                request.BranchId, zone.Value);
        }

        var day = date ?? BranchDay.Today(zone.Value, nowUtc);
        var (fromUtc, toUtc) = BranchDay.ToUtcRange(day, zone.Value);

        var result = await appointments.ListForBranchAsync(
            request.BranchId, fromUtc, toUtc, status, page, pageSize, cancellationToken);

        IReadOnlyDictionary<Guid, CustomerInfo> customers = new Dictionary<Guid, CustomerInfo>();
        var customersUnavailable = false;
        if (result.Items.Count > 0)
        {
            var ids = result.Items.Select(a => a.CustomerId).Distinct().ToList();
            var lookup = await authClient.LookupAsync(ids, cancellationToken);
            if (lookup.IsSuccess)
            {
                customers = lookup.Value!;
            }
            else
            {
                customersUnavailable = true;
            }
        }

        var items = result.Items
            .Select(a => BranchAppointmentResponse.From(a, customers.GetValueOrDefault(a.CustomerId)))
            .ToList();

        return Result<BranchAppointmentsPage>.Success(
            new BranchAppointmentsPage(items, page, pageSize, result.TotalCount, customersUnavailable));
    }
}
