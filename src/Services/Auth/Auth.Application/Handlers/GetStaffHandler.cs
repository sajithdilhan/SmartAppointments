using Auth.Application.Abstractions;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using SmartAppointments.BuildingBlocks.Enums;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class GetStaffHandler(IUserRepository users) : IRequestHandler<GetStaffQuery, Result<StaffResponse>>
{
    public async Task<Result<StaffResponse>> Handle(GetStaffQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.Id, cancellationToken);

        // Inactive staff are returned; unknown, non-staff and deleted accounts are all the same 404.
        if (user is null || user.Role != UserRole.Staff || user.IsDeleted)
        {
            return Result<StaffResponse>.Failure(new Error(404, $"Staff member '{request.Id}' was not found."));
        }

        return Result<StaffResponse>.Success(StaffResponse.From(user));
    }
}
