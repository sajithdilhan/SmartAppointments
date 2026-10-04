using Booking.Application.Abstractions;
using Booking.Application.Models;
using Booking.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure.Persistence;

public class AppointmentRepository(ApplicationDbContext context) : IAppointmentRepository
{
    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public Task<Appointment?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public Task<bool> HasOverlappingBookedAsync(
        Guid customerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        return OverlapQuery(customerId, startUtc, endUtc).AnyAsync(cancellationToken);
    }

    public async Task<AddAppointmentOutcome> TryAddBookedAsync(
        Appointment appointment, IdempotencyRecord completedRecord, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Serialises this customer's bookings so two requests with different keys cannot both pass
        // the overlap check below. Different customers hash to different keys and do not contend.
        // Held until the transaction ends, and never across the HTTP call to Availability.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({appointment.CustomerId}::text, 0))",
            cancellationToken);

        // The handler's pre-check ran before the remote reserve call; this one is the authority.
        if (await OverlapQuery(appointment.CustomerId, appointment.StartUtc, appointment.EndUtc).AnyAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return AddAppointmentOutcome.Overlap;
        }

        context.Appointments.Add(appointment);
        await context.SaveChangesAsync(cancellationToken);

        var completed = await IdempotencyRepository.CompleteOwnedAsync(context, completedRecord, cancellationToken);
        if (completed == 0)
        {
            // The claim was lost (its lease ended and another request took it over). Keeping the
            // appointment anyway could book one key twice, so the transaction rolls back and the
            // caller's failure handling compensates.
            throw new InvalidOperationException("The idempotency claim is no longer held by this request.");
        }

        await transaction.CommitAsync(cancellationToken);
        return AddAppointmentOutcome.Saved;
    }

    public async Task<AppointmentPage> ListForCustomerAsync(
        Guid customerId, AppointmentStatus? status, AppointmentTimeFilter? when, DateTime nowUtc,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        // Served by IX_Appointments_CustomerId_StartUtc; the Id tie-break is finished by an incremental sort.
        var query = context.Appointments.AsNoTracking().Where(a => a.CustomerId == customerId);
        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        // An appointment starting exactly at "now" is upcoming, never both or neither.
        if (when == AppointmentTimeFilter.Upcoming)
        {
            query = query.Where(a => a.StartUtc >= nowUtc);
        }
        else if (when == AppointmentTimeFilter.Past)
        {
            query = query.Where(a => a.StartUtc < nowUtc);
        }

        var total = await query.CountAsync(cancellationToken);

        // A long, because page can be up to int.MaxValue. Past the end there is nothing to read, and
        // once this passes skip < total, so the int cast below is safe.
        var skip = (long)(page - 1) * pageSize;
        if (skip >= total)
        {
            return new AppointmentPage([], total);
        }

        var ordered = when == AppointmentTimeFilter.Upcoming
            ? query.OrderBy(a => a.StartUtc).ThenBy(a => a.Id)
            : query.OrderByDescending(a => a.StartUtc).ThenBy(a => a.Id);

        var items = await ordered.Skip((int)skip).Take(pageSize).ToListAsync(cancellationToken);
        return new AppointmentPage(items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return context.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Appointment> OverlapQuery(Guid customerId, DateTime startUtc, DateTime endUtc)
    {
        return context.Appointments
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId
                        && a.Status == AppointmentStatus.Booked
                        && a.StartUtc < endUtc
                        && a.EndUtc > startUtc);
    }
}
