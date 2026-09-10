// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Constants;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Classifies a failure raised while deleting a time-job tree as a conflict the whole scope may retry with fresh
/// discovery. The non-cascading Parent/Children foreign key is the atomicity fence: a child appended after discovery
/// surfaces as a foreign-key violation on the parent delete, and the attempt rolls back rather than committing a
/// partial tree.
/// </summary>
internal static class JobsTreeDeleteConflicts
{
    /// <summary>
    /// Returns whether <paramref name="exception"/> is a retryable tree-delete conflict for the given EF provider.
    /// </summary>
    /// <remarks>
    /// The commit phase is never retried: a commit that succeeded on the server but failed on the wire would
    /// otherwise be re-run and report zero rows for a tree that is already gone. Cancellation is never retried
    /// either, including a driver that reports a cancel as a database exception while the caller token is already
    /// cancelled. Provider codes are matched by name because this package references no driver assemblies, and
    /// <see cref="DbException.IsTransient"/> is only an additional signal: SQL Server's exception type overrides
    /// neither it nor <see cref="DbException.SqlState"/>, so its deadlock, constraint, and snapshot conflicts are
    /// matched on the reflected error number. Serialization (40001) and snapshot-conflict (3960) codes stay in the
    /// set so the retry still works when a consumer raises the default isolation level.
    /// </remarks>
    internal static bool IsRetryableTreeDeleteFailure(
        string? providerName,
        Exception exception,
        bool commitStarted,
        CancellationToken cancellationToken
    )
    {
        if (commitStarted || cancellationToken.IsCancellationRequested || exception is OperationCanceledException)
        {
            return false;
        }

        // Walk outer-first and stop at the FIRST DbException: drivers report a dropped connection as a transient
        // DbException wrapping the underlying IOException or SocketException, so the innermost exception would be
        // the socket error and the transient signal on the outer driver exception would be lost.
        if (_FindDatabaseException(exception) is not { } databaseException)
        {
            return false;
        }

        if (databaseException.IsTransient)
        {
            return true;
        }

        if (string.Equals(providerName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            return databaseException.SqlState
                is SqlErrorCodes.PostgreSql.ForeignKeyViolation
                    or SqlErrorCodes.PostgreSql.SerializationFailure
                    or SqlErrorCodes.PostgreSql.DeadlockDetected;
        }

        if (string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
        {
            var number = databaseException.GetType().GetProperty("Number")?.GetValue(databaseException);
            return number
                is SqlErrorCodes.SqlServer.ConstraintViolation
                    or SqlErrorCodes.SqlServer.DeadlockVictim
                    or SqlErrorCodes.SqlServer.SnapshotUpdateConflict;
        }

        return false;
    }

    private static DbException? _FindDatabaseException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException databaseException)
            {
                return databaseException;
            }
        }

        return null;
    }
}
