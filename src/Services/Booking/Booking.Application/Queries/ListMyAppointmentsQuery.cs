using Booking.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Queries;

/// <summary>
/// The four filter and paging values are the raw query strings, so that a bad value reaches the
/// validator instead of failing model binding. <c>CallerId</c> comes from the token.
/// </summary>
public sealed record ListMyAppointmentsQuery(
    Guid CallerId, string? Status, string? When, string? Page, string? PageSize)
    : IRequest<Result<PagedResponse<AppointmentResponse>>>;
