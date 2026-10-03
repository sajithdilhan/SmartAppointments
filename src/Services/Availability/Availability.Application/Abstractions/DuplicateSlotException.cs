namespace Availability.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="ISlotRepository.SaveChangesAsync"/> when the unique index on branch,
/// service type and start time rejects slots that a concurrent generation created after the
/// handler's overlap check.
/// </summary>
public sealed class DuplicateSlotException : Exception
{
    public DuplicateSlotException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
