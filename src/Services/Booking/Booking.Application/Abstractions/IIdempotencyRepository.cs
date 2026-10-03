using Booking.Domain.Entities;

namespace Booking.Application.Abstractions;

/// <summary>What happened when a request tried to claim its Idempotency-Key.</summary>
public abstract record ClaimResult
{
    private ClaimResult()
    {
    }

    /// <summary>This request owns the key. <c>Record.AppointmentId</c> is the id to use, which is an earlier attempt's when the key was taken over.</summary>
    public sealed record Claimed(IdempotencyRecord Record) : ClaimResult;

    /// <summary>A completed, unexpired record with an equal request hash: return the stored outcome.</summary>
    public sealed record Replay(int StatusCode, string ResponseBody) : ClaimResult;

    public sealed record HashMismatch : ClaimResult;

    public sealed record InProgress : ClaimResult;
}

public interface IIdempotencyRepository
{
    Task<ClaimResult> ClaimAsync(
        Guid userId, string key, string requestHash, Guid appointmentId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Stores the outcome. Does nothing if the caller no longer owns the record.</summary>
    Task CompleteAsync(
        IdempotencyRecord record, int statusCode, string responseBody, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Deletes the record only while it is still in progress and still the caller's.</summary>
    Task RemoveAsync(IdempotencyRecord record, CancellationToken cancellationToken);
}
