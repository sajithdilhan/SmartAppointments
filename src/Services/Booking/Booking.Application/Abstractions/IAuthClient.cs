using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Abstractions;

public sealed record CustomerInfo(string FirstName, string LastName, string PhoneNumber);

/// <summary>Booking's view of Auth's internal API.</summary>
public interface IAuthClient
{
    /// <summary>Failure(503) on any outage; unknown ids are simply absent from the dictionary.</summary>
    Task<Result<IReadOnlyDictionary<Guid, CustomerInfo>>> LookupAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
