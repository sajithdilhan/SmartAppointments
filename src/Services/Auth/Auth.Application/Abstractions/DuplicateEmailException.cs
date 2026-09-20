namespace Auth.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IUserRepository.SaveChangesAsync"/> when the commit is rejected because
/// another user already holds the email address. The read-then-write duplicate check in the
/// registration handler cannot see a concurrent registration that has not committed yet, so the
/// unique index is the real arbiter and this exception is how its verdict reaches the handler
/// without dragging a persistence-specific exception type into the Application layer.
/// </summary>
public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
