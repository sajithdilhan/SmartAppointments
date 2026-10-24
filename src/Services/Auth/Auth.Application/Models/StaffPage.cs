namespace Auth.Application.Models;

public sealed record StaffPage(IReadOnlyList<StaffResponse> Items, int Page, int PageSize, int TotalCount);
