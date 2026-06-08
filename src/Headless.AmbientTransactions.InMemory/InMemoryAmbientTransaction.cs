// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;

namespace Headless.AmbientTransactions.InMemory;

/// <summary>
/// In-process ambient transaction implementation. It provides commit/rollback coordination, not durability.
/// </summary>
[PublicAPI]
public sealed class InMemoryAmbientTransaction(ICurrentAmbientTransaction currentAmbientTransaction)
    : AmbientTransactionBase(currentAmbientTransaction)
{
    private static readonly object _TransactionSentinel = new();

    public void Begin(bool autoCommit = false)
    {
        DbTransaction = _TransactionSentinel;
        AutoCommit = autoCommit;
    }

    public override void Commit()
    {
        DrainCommitWork();
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await DrainCommitWorkAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Rollback()
    {
        DiscardCommitWork();
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        DiscardCommitWork();
        return Task.CompletedTask;
    }
}
