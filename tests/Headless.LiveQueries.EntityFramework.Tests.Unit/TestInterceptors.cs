// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests;

/// <summary>Makes a commit throw after the database has applied it, as a connection lost before the answer does.</summary>
internal sealed class CommitAnswerLostInterceptor : DbTransactionInterceptor
{
    /// <summary>Off while a test prepares its data, so only the commit under test loses its answer.</summary>
    public bool Armed { get; set; }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        return Armed
            ? throw new TimeoutException("The connection timed out before the commit was acknowledged.")
            : Task.CompletedTask;
    }
}

/// <summary>Makes a commit throw before anything is sent to the database, so the transaction is known not to commit.</summary>
internal sealed class CommitRefusedInterceptor : DbTransactionInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        return Armed
            ? throw new InvalidOperationException("The commit was refused before it was sent.")
            : ValueTask.FromResult(result);
    }
}

/// <summary>Reads a number at the moment a commit is about to be sent.</summary>
internal sealed class BeforeCommitProbe : DbTransactionInterceptor
{
    public Func<long>? Read { get; set; }

    public List<long> Seen { get; } = [];

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (Read is { } read)
        {
            Seen.Add(read());
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>Makes a save throw after the database has stored it, as a later interceptor or an event handler can.</summary>
internal sealed class AfterSaveThrows : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        return Armed
            ? throw new InvalidOperationException("A step after the save was stored failed.")
            : ValueTask.FromResult(result);
    }
}

/// <summary>Fails the second command of a save once with a fault the retrying strategy below retries.</summary>
internal sealed class SecondSaveCommandFailsOnce : DbCommandInterceptor
{
    private int _saveCommands;

    public bool Armed { get; set; }

    public int Failures { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        if (Armed && eventData.CommandSource is CommandSource.SaveChanges && ++_saveCommands == 2)
        {
            Failures++;

            throw new TransientMarkerException();
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>A marker fault the retrying strategy retries.</summary>
internal sealed class TransientMarkerException() : Exception("Simulated transient failure.");

/// <summary>An execution strategy that retries <see cref="TransientMarkerException" /> once with no delay.</summary>
internal sealed class RetryOnceExecutionStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception) => exception is TransientMarkerException;
}
