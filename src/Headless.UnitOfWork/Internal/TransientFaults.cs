// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The classification steps shared by <see cref="RelationalTransientFaults" /> and the narrower classifiers that
/// build on it (the Jobs tree delete): the cancellation veto and the outer-first database-exception walk.
/// </summary>
internal static class TransientFaults
{
    /// <summary>Whether the failure is, or was observed during, a cancellation.</summary>
    public static bool IsCancellation(Exception exception, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested || exception is OperationCanceledException;
    }

    /// <summary>The outermost <see cref="DbException" /> in <paramref name="exception" />'s inner-exception chain.</summary>
    public static DbException? FindDatabaseException(Exception exception)
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
