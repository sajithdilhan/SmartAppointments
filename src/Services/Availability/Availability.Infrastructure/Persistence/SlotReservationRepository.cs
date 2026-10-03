using Availability.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Persistence;

/// <summary>
/// The only code that changes <c>Slot.ReservedCount</c>. Both write methods run one transaction
/// around one conditional statement, so there is no read-then-write and no lost update.
/// <c>ExecuteUpdateAsync</c> and <c>ExecuteDeleteAsync</c> bypass the change tracker, so no
/// <c>Slot</c> is loaded and written back.
/// </summary>
public class SlotReservationRepository(ApplicationDbContext context) : ISlotReservationRepository
{
    public Task<bool> ExistsAsync(Guid slotId, Guid appointmentId, CancellationToken cancellationToken)
    {
        return context.SlotReservations
            .AsNoTracking()
            .AnyAsync(r => r.SlotId == slotId && r.AppointmentId == appointmentId, cancellationToken);
    }

    public async Task<ReserveOutcome> ReserveAsync(
        Guid slotId, Guid appointmentId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The unique key, not application logic, decides duplicates. If an identical request is
        // mid-transaction this waits for it to commit or roll back, then decides.
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "SlotReservations" ("SlotId", "AppointmentId", "CreatedAtUtc")
            VALUES ({slotId}, {appointmentId}, {nowUtc})
            ON CONFLICT ("SlotId", "AppointmentId") DO NOTHING
            """,
            cancellationToken);

        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return ReserveOutcome.AlreadyReserved;
        }

        // Two requests racing for the last place serialise on the row lock this takes: the second
        // re-evaluates the predicate after the first commits, matches no row, and gets Full.
        var updated = await context.Slots
            .Where(s => s.Id == slotId && s.ReservedCount < s.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedCount, x => x.ReservedCount + 1), cancellationToken);

        if (updated == 0)
        {
            // Slot full: rolling back removes the reservation row inserted a moment ago.
            await transaction.RollbackAsync(cancellationToken);
            return ReserveOutcome.Full;
        }

        await transaction.CommitAsync(cancellationToken);
        return ReserveOutcome.Reserved;
    }

    public async Task<ReleaseOutcome> ReleaseAsync(Guid slotId, Guid appointmentId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Two simultaneous releases of one reservation: the second DELETE waits on the row lock,
        // then finds nothing, so only one decrement happens.
        var deleted = await context.SlotReservations
            .Where(r => r.SlotId == slotId && r.AppointmentId == appointmentId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return ReleaseOutcome.NotReserved;
        }

        await context.Slots
            .Where(s => s.Id == slotId && s.ReservedCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedCount, x => x.ReservedCount - 1), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ReleaseOutcome.Released;
    }
}
