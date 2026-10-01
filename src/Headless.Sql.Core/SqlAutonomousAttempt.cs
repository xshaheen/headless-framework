// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

/// <summary>
/// One attempt of <see cref="SqlAutonomousTransaction.RetryAsync{T}" />: it records whether the attempt's commit
/// started, which decides whether its fault may be retried.
/// </summary>
[PublicAPI]
public sealed class SqlAutonomousAttempt
{
    /// <summary>Gets whether the attempt started its commit; a fault raised from then on is never retried.</summary>
    public bool CommitStarted { get; private set; }

    /// <summary>
    /// Marks the commit as started. Call it immediately before the commit: a fault raised by the commit may come
    /// from a transaction the server already committed, so a retry could apply the attempt twice.
    /// </summary>
    public void MarkCommitStarted() => CommitStarted = true;
}
