// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.AmbientTransactions;

namespace Tests;

public abstract class AmbientTransactionFixtureBase
{
    public abstract ICurrentAmbientTransaction CurrentAmbientTransaction { get; }

    public virtual ValueTask ResetAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public abstract ValueTask<IAmbientTransaction> BeginTransactionAsync(
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    );

    public virtual ValueTask<IAmbientTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return BeginTransactionAsync(autoCommit, cancellationToken);
    }

    public virtual ValueTask CompleteExternalCommitAsync(
        IAmbientTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        transaction.CompleteExternally();
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask AssertCommittedWorkVisibleAsync(
        string workKey,
        CancellationToken cancellationToken = default
    ) => ValueTask.CompletedTask;

    public virtual ValueTask AssertRolledBackWorkAbsentAsync(
        string workKey,
        CancellationToken cancellationToken = default
    ) => ValueTask.CompletedTask;
}
