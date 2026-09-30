// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Constants;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;

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
    /// The unit-of-work transient classifier supplies the shared rules (<see cref="RelationalTransientFaults" />);
    /// this adds the foreign-key violation that signals a concurrent append, which only a tree delete may retry. The
    /// commit phase is never retried: a commit that succeeded on the server but failed on the wire would otherwise be
    /// re-run and report zero rows for a tree that is already gone. Provider codes are gated by the EF provider name,
    /// so a driver exception is never read in another provider's code space.
    /// </remarks>
    internal static bool IsRetryableTreeDeleteFailure(
        string? providerName,
        Exception exception,
        bool commitStarted,
        CancellationToken cancellationToken
    )
    {
        if (commitStarted || TransientFaults.IsCancellation(exception, cancellationToken))
        {
            return false;
        }

        if (TransientFaults.FindDatabaseException(exception) is not { } databaseException)
        {
            return false;
        }

        if (databaseException.IsTransient)
        {
            return true;
        }

        if (string.Equals(providerName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            return PostgreSqlTransientFaults.IsTransientSqlState(databaseException.SqlState)
                || databaseException.SqlState is SqlErrorCodes.PostgreSql.ForeignKeyViolation;
        }

        if (string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
        {
            // Every error in the batch counts: SqlClient reports several per exception, and the deadlock or the
            // foreign-key conflict is not always the first.
            foreach (var number in SqlServerTransientFaults.GetErrorNumbers(databaseException))
            {
                if (
                    SqlServerTransientFaults.IsTransientNumber(number)
                    || number is SqlErrorCodes.SqlServer.ConstraintViolation
                )
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }
}
