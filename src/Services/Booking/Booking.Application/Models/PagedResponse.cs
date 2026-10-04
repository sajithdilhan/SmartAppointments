namespace Booking.Application.Models;

/// <summary><c>Page</c> and <c>PageSize</c> are the values applied, defaults included.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
