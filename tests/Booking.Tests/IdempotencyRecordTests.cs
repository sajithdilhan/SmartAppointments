using Booking.Domain.Entities;

namespace Booking.Tests;

public class IdempotencyRecordTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);

    private static IdempotencyRecord NewRecord() =>
        IdempotencyRecord.Claim(Guid.NewGuid(), "key-1", new string('a', 64), Guid.NewGuid(), Now);

    [Fact]
    public void Claim_Starts_In_Progress_With_A_Two_Minute_Lease()
    {
        var user = Guid.NewGuid();
        var appointment = Guid.NewGuid();

        var record = IdempotencyRecord.Claim(user, "key-1", "hash", appointment, Now);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(user, record.UserId);
        Assert.Equal("key-1", record.IdempotencyKey);
        Assert.Equal("hash", record.RequestHash);
        Assert.Equal(appointment, record.AppointmentId);
        Assert.Equal(IdempotencyState.InProgress, record.State);
        Assert.Null(record.StatusCode);
        Assert.Null(record.ResponseBody);
        Assert.Equal(Now, record.CreatedAtUtc);
        Assert.Equal(Now.AddMinutes(2), record.ExpiresAtUtc);
    }

    [Fact]
    public void Claim_Trims_Timestamps_To_Microseconds()
    {
        var record = IdempotencyRecord.Claim(Guid.NewGuid(), "k", "h", Guid.NewGuid(), Now.AddTicks(7));

        Assert.Equal(Now, record.CreatedAtUtc);
    }

    [Fact]
    public void Claim_Rejects_Missing_Values()
    {
        Assert.Throws<ArgumentException>(() => IdempotencyRecord.Claim(Guid.Empty, "k", "h", Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => IdempotencyRecord.Claim(Guid.NewGuid(), " ", "h", Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => IdempotencyRecord.Claim(Guid.NewGuid(), "k", "", Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => IdempotencyRecord.Claim(Guid.NewGuid(), "k", "h", Guid.Empty, Now));
    }

    [Fact]
    public void Complete_Stores_Outcome_And_Extends_To_Twenty_Four_Hours()
    {
        var record = NewRecord();
        var later = Now.AddSeconds(5);

        record.Complete(201, "{\"id\":1}", later);

        Assert.Equal(IdempotencyState.Completed, record.State);
        Assert.Equal(201, record.StatusCode);
        Assert.Equal("{\"id\":1}", record.ResponseBody);
        Assert.Equal(later.AddHours(24), record.ExpiresAtUtc);
        Assert.Equal(Now, record.CreatedAtUtc);
    }

    [Fact]
    public void IsExpired_Flips_At_The_Boundary()
    {
        var record = NewRecord();

        Assert.False(record.IsExpired(Now.AddMinutes(2).AddTicks(-10)));
        Assert.True(record.IsExpired(Now.AddMinutes(2)));
        Assert.True(record.IsExpired(Now.AddMinutes(3)));
    }
}
