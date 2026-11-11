// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Headless.LiveQueries.Internal;

/// <summary>Tells the tracker how each EF Core transaction of a tracked context ended.</summary>
internal sealed class LiveTransactionInterceptor(LiveWriteTracker tracker) : DbTransactionInterceptor
{
    // The value EF Core puts in TransactionErrorEventData.Action for a commit that threw.
    private const string _CommitAction = "Commit";

    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result
    )
    {
        _Started(eventData);

        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        _Started(eventData);

        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        tracker.TransactionEnded(eventData.Context, transaction, mayBeDurable: true);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        tracker.TransactionEnded(eventData.Context, transaction, mayBeDurable: true);

        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        tracker.TransactionEnded(eventData.Context, transaction, mayBeDurable: false);
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        tracker.TransactionEnded(eventData.Context, transaction, mayBeDurable: false);

        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        _Failed(transaction, eventData);
    }

    public override Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        _Failed(transaction, eventData);

        return Task.CompletedTask;
    }

    private void _Started(TransactionEndEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            tracker.TransactionStarted(context);
        }
    }

    private void _Failed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        // A commit that threw may still have reached the database, so its writes are announced, as whole tables
        // because what it stored is not known. A rollback that threw stored nothing either way.
        var commit = string.Equals(eventData.Action, _CommitAction, StringComparison.Ordinal);
        tracker.TransactionEnded(eventData.Context, transaction, mayBeDurable: commit, inDoubt: commit);
    }
}
