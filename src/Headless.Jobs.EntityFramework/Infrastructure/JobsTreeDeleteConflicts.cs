// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

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
    /// Returns whether <paramref name="exception"/> is a retryable tree-delete conflict.
    /// </summary>
    /// <remarks>
    /// The unit-of-work transient classifier supplies the shared rules (<see cref="RelationalTransientFaults" />);
    /// this adds the foreign-key violation that signals a concurrent append, which only a tree delete may retry. The
    /// commit phase is never retried: a commit that succeeded on the server but failed on the wire would otherwise be
    /// re-run and report zero rows for a tree that is already gone. The foreign-key classifier reads each driver's
    /// codes only from that driver's exception type, so no provider gate is needed here.
    /// </remarks>
    internal static bool IsRetryableTreeDeleteFailure(
        Exception exception,
        bool commitStarted,
        CancellationToken cancellationToken
    )
    {
        // The transient classifier vetoes a cancellation itself, but the foreign-key check below must honor it too.
        if (commitStarted || cancellationToken.IsCancellationRequested || exception is OperationCanceledException)
        {
            return false;
        }

        if (RelationalTransientFaults.IsTransient(exception, cancellationToken))
        {
            return true;
        }

        return RelationalFaults.IsReferenceViolation(exception);
    }
}
