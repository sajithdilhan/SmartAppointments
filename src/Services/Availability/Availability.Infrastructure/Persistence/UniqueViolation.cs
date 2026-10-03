using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Availability.Infrastructure.Persistence;

internal static class UniqueViolation
{
    /// <summary>
    /// True when the commit failed on the named unique index. The context holds several unique
    /// indexes, so the SQLSTATE alone (23505) no longer says which rule was broken.
    /// </summary>
    public static bool IsOn(DbUpdateException ex, string indexName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == indexName;
}
