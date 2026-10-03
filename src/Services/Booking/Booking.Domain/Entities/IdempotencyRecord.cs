namespace Booking.Domain.Entities;

public sealed class IdempotencyRecord
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromHours(24);

    private IdempotencyRecord()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;

    // The id this request will give its appointment; a retry after a crash reuses it so a
    // reservation Availability already made is found instead of leaked.
    public Guid AppointmentId { get; private set; }
    public IdempotencyState State { get; private set; }
    public int? StatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // Two meanings, one question ("is this record still binding?"): for an InProgress record it is
    // the end of the lease, for a Completed one the end of the replay window.
    public DateTime ExpiresAtUtc { get; private set; }

    public static IdempotencyRecord Claim(Guid userId, string key, string hash, Guid appointmentId, DateTime nowUtc)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Idempotency key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("Request hash is required.", nameof(hash));
        if (appointmentId == Guid.Empty) throw new ArgumentException("Appointment id is required.", nameof(appointmentId));

        var now = TruncateToMicroseconds(nowUtc);
        return new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            IdempotencyKey = key,
            RequestHash = hash,
            AppointmentId = appointmentId,
            State = IdempotencyState.InProgress,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + Lease
        };
    }

    public void Complete(int statusCode, string responseBody, DateTime nowUtc)
    {
        State = IdempotencyState.Completed;
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ExpiresAtUtc = TruncateToMicroseconds(nowUtc) + ReplayWindow;
    }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    // PostgreSQL keeps microseconds; trimming here keeps in-memory values equal to what the
    // database returns, because the repository compares them to tell whose lease it is.
    public static DateTime TruncateToMicroseconds(DateTime value) => value.AddTicks(-(value.Ticks % 10));
}
