using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Queries;

/// <summary>
/// The four filter and paging values are the raw query strings, so that a bad value reaches the
/// validator instead of failing model binding.
/// </summary>
public sealed record ListStaffQuery(string? BranchId, string? IsActive, string? Page, string? PageSize)
    : IRequest<Result<StaffPage>>;
