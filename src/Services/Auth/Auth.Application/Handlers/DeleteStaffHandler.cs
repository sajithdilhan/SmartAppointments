using Auth.Application.Abstractions;
using Auth.Application.Commands;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Enums;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

public class DeleteStaffHandler(
    IUserRepository userRepository,
    TimeProvider timeProvider,
    ILogger<DeleteStaffHandler> logger) : IRequestHandler<DeleteStaffCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(DeleteStaffCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetForUpdateByIdAsync(request.Id, cancellationToken);
        if (user is null || user.Role != UserRole.Staff || user.IsDeleted)
        {
            return Result<Unit>.Failure(new Error(404, $"Staff member '{request.Id}' was not found."));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        user.MarkDeleted(now);

        var revoked = await userRepository.SaveAndRevokeSessionsAsync(user, now, cancellationToken);

        logger.LogInformation("Deleted staff {UserId}; revoked {Revoked} refresh tokens", user.Id, revoked);
        return Result<Unit>.Success(Unit.Value);
    }
}
