namespace Availability.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IServiceTypeRepository.SaveChangesAsync"/> when the unique index on the
/// code rejects a concurrent create that the handler's pre-check could not see.
/// </summary>
public sealed class DuplicateServiceTypeCodeException : Exception
{
    public DuplicateServiceTypeCodeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
