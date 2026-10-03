namespace Booking.Tests;

/// <summary>
/// A clock that stays where the test put it, so "now" in the handlers is deterministic.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
