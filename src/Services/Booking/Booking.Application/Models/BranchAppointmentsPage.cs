namespace Booking.Application.Models;

/// <summary><c>Page</c> and <c>PageSize</c> are the values applied. <c>CustomersUnavailable</c> is true when the customer lookup failed.</summary>
public sealed record BranchAppointmentsPage(
    IReadOnlyList<BranchAppointmentResponse> Items, int Page, int PageSize, int TotalCount, bool CustomersUnavailable);
