using Booking.Application.Abstractions;
using Booking.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure.Persistence;

/// <summary>
/// Claims, completes and removes idempotency records. Nothing here relies on a tracked entity
/// surviving: a record is "owned" by whoever holds its Id and CreatedAtUtc while it is InProgress,
/// and every write is one conditional statement on exactly that.
/// </summary>
public class IdempotencyRepository(ApplicationDbContext context) : IIdempotencyRepository
{
    public const string KeyIndexName = "IX_IdempotencyRecords_UserId_IdempotencyKey";

    public async Task<ClaimResult> ClaimAsync(
        Guid userId, string key, string requestHash, Guid appointmentId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var now = IdempotencyRecord.TruncateToMicroseconds(nowUtc);

        // Twice at most: the second pass is for a row that vanished between a failed insert and the read.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var record = IdempotencyRecord.Claim(userId, key, requestHash, appointmentId, now);
            context.IdempotencyRecords.Add(record);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                context.Entry(record).State = EntityState.Detached;
                return new ClaimResult.Claimed(record);
            }
            catch (DbUpdateException ex) when (UniqueViolation.IsOn(ex, KeyIndexName))
            {
                // The key exists: the loser of the race reads the winner's row below.
                context.Entry(record).State = EntityState.Detached;
            }

            var existing = await context.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.IdempotencyKey == key, cancellationToken);
            if (existing is null)
            {
                continue;
            }

            if (!existing.IsExpired(now))
            {
                if (existing.RequestHash != requestHash)
                {
                    return new ClaimResult.HashMismatch();
                }

                return existing.State == IdempotencyState.Completed
                    ? new ClaimResult.Replay(existing.StatusCode!.Value, existing.ResponseBody!)
                    : new ClaimResult.InProgress();
            }

            return await TakeOverAsync(existing, requestHash, appointmentId, now, cancellationToken);
        }

        return new ClaimResult.InProgress();
    }

    // One conditional statement, guarded by the expiry we observed, so of several requests finding
    // the same expired row exactly one wins.
    private async Task<ClaimResult> TakeOverAsync(
        IdempotencyRecord existing, string requestHash, Guid newAppointmentId, DateTime now, CancellationToken cancellationToken)
    {
        // A crashed first attempt may already have reserved its appointment id in Availability, so an
        // in-progress record keeps it; a completed one is finished business and gets the new id.
        var appointmentId = existing.State == IdempotencyState.InProgress ? existing.AppointmentId : newAppointmentId;
        var expiresAt = now + IdempotencyRecord.Lease;
        var observedExpiry = existing.ExpiresAtUtc;

        var updated = await context.IdempotencyRecords
            .Where(r => r.Id == existing.Id && r.ExpiresAtUtc == observedExpiry)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RequestHash, requestHash)
                .SetProperty(r => r.AppointmentId, appointmentId)
                .SetProperty(r => r.State, IdempotencyState.InProgress)
                .SetProperty(r => r.StatusCode, (int?)null)
                .SetProperty(r => r.ResponseBody, (string?)null)
                .SetProperty(r => r.CreatedAtUtc, now)
                .SetProperty(r => r.ExpiresAtUtc, expiresAt),
                cancellationToken);
        if (updated == 0)
        {
            return new ClaimResult.InProgress();
        }

        // Reload so the handler holds the record exactly as stored, including the id it must reuse.
        var taken = await context.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == existing.Id, cancellationToken);
        return taken is null ? new ClaimResult.InProgress() : new ClaimResult.Claimed(taken);
    }

    public async Task CompleteAsync(
        IdempotencyRecord record, int statusCode, string responseBody, DateTime nowUtc, CancellationToken cancellationToken)
    {
        record.Complete(statusCode, responseBody, nowUtc);
        await CompleteOwnedAsync(context, record, cancellationToken);
    }

    public async Task RemoveAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        // A slow request must not delete a record a later request has taken over or finished.
        var createdAt = record.CreatedAtUtc;
        await context.IdempotencyRecords
            .Where(r => r.Id == record.Id && r.State == IdempotencyState.InProgress && r.CreatedAtUtc == createdAt)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the outcome already set on <paramref name="record"/>, only if it is still the caller's
    /// in-progress claim. Returns the rows changed (0 means the claim was lost).
    /// </summary>
    internal static Task<int> CompleteOwnedAsync(
        ApplicationDbContext context, IdempotencyRecord record, CancellationToken cancellationToken)
    {
        var createdAt = record.CreatedAtUtc;
        var statusCode = record.StatusCode;
        var body = record.ResponseBody;
        var expiresAt = record.ExpiresAtUtc;
        return context.IdempotencyRecords
            .Where(r => r.Id == record.Id && r.State == IdempotencyState.InProgress && r.CreatedAtUtc == createdAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.State, IdempotencyState.Completed)
                .SetProperty(r => r.StatusCode, statusCode)
                .SetProperty(r => r.ResponseBody, body)
                .SetProperty(r => r.ExpiresAtUtc, expiresAt),
                cancellationToken);
    }
}
