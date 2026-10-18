using Booking.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Queries;

/// <summary>
/// Date, status, page and page size are the raw query strings, so a bad value reaches the validator.
/// The caller's role and branch come from the token.
/// </summary>
public sealed record ListBranchAppointmentsQuery(
    Guid BranchId, string? Date, string? Status, string? Page, string? PageSize,
    string CallerRole, Guid? CallerBranchId)
    : IRequest<Result<BranchAppointmentsPage>>;
