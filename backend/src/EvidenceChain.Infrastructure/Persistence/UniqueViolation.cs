using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Persistence;

internal static class UniqueViolation
{
    /// <summary>
    /// The unique index or constraint a failed save collided with (SQL Server errors 2601 and 2627), so callers can tell
    /// a replayed key from a second pending transfer or a concurrent append. Null for any other failure.
    /// </summary>
    public static string? IndexOf(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sql ? NameIn(sql.Message) : null;

    // 2601: "... with unique index 'UX_…'. The duplicate key value is (…)."   2627: "Violation of UNIQUE KEY constraint 'UQ_…'. …"
    private static string? NameIn(string message)
    {
        foreach (var marker in new[] { "unique index '", "constraint '" })
        {
            var start = message.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                continue;
            start += marker.Length;
            var end = message.IndexOf('\'', start);
            if (end > start)
                return message[start..end];
        }
        return null;
    }
}
