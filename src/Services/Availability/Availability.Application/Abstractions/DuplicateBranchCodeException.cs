namespace Availability.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IBranchRepository.SaveChangesAsync"/> when the commit is rejected because
/// another branch already holds the code. The handler's pre-check cannot see a concurrent create
/// that has not committed yet, so the unique index is the real arbiter and this exception is how
/// its verdict reaches the handler without a persistence-specific exception type in this layer.
/// </summary>
public sealed class DuplicateBranchCodeException : Exception
{
    public DuplicateBranchCodeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
