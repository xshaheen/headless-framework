// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>
/// Exception messages the save pipeline and the outbox dispatcher share, so a mis-wired save fails with the
/// same remedy wherever the check trips.
/// </summary>
internal static class HeadlessUnitOfWorkMessages
{
    /// <summary>
    /// A save inside a caller-owned transaction carried integration events, but no unit of work owns that
    /// transaction (none is active, or the active one is resource-less or on another resource).
    /// </summary>
    public const string CallerOwnedTransactionWithoutUnitOfWork =
        "SaveChanges ran inside a caller-owned transaction that no unit of work owns, so integration events and jobs "
        + "would dispatch non-atomically. Begin the unit of work on this context (IUnitOfWorkFactory.BeginAsync(db)) "
        + "before beginning the transaction, or enlist the transaction with Enlist(db, transaction).";
}
