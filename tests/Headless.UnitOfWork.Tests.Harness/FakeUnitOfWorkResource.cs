// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// A fake owned or observed <see cref="IUnitOfWorkResource" /> for conformance scenarios: records commit /
/// rollback calls and can be scripted to fault, without standing up a database.
/// </summary>
public sealed class FakeUnitOfWorkResource(bool isOwned = true) : IRelationalUnitOfWorkResource
{
    private int _commitCalls;
    private int _rollbackCalls;

    public Exception? CommitFault { get; set; }

    public Exception? RollbackFault { get; set; }

    public bool TransactionCompleted { get; set; }

    public int CommitCalls => Volatile.Read(ref _commitCalls);

    public int RollbackCalls => Volatile.Read(ref _rollbackCalls);

    public bool IsOwned { get; } = isOwned;

    public bool IsTransactionCompleted => TransactionCompleted;

    public DbConnection Connection => null!;

    public DbTransaction Transaction => null!;

    public ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _commitCalls);
        TransactionCompleted = true;

        return CommitFault is null ? ValueTask.CompletedTask : ValueTask.FromException(CommitFault);
    }

    public ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _rollbackCalls);
        TransactionCompleted = true;

        return RollbackFault is null ? ValueTask.CompletedTask : ValueTask.FromException(RollbackFault);
    }
}

/// <summary>
/// A relational handle with no live connection or transaction — enough to prove a unit carries the resource it
/// was begun with without standing up a database.
/// </summary>
public sealed class StubRelationalUnitOfWorkResource : IRelationalUnitOfWorkResource
{
    public bool IsOwned => false;

    public bool IsTransactionCompleted => false;

    public DbConnection Connection => null!;

    public DbTransaction Transaction => null!;

    public ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
