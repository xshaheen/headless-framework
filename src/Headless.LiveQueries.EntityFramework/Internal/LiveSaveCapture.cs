// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.LiveQueries.Internal;

/// <summary>What one <c>SaveChanges</c> call that has not returned yet is writing.</summary>
/// <param name="contextId">
/// The context instance and pool lease the save runs on. A pooled context that is leased again carries a new lease,
/// which is how a capture left behind by a save that never reported its end is told from the current one.
/// </param>
internal sealed class LiveSaveCapture(DbContextId contextId)
{
    private readonly Lock _lock = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private bool _transactionStarted;
    private bool _commandSucceeded;
    private bool _announced;
    private bool _announcedForTransaction;

    public DbContextId ContextId { get; } = contextId;

    public void Add(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            _keys.UnionWith(keys);
        }
    }

    /// <summary>Records that EF Core began a transaction during the save, which is the save's own.</summary>
    public void TransactionStarted()
    {
        lock (_lock)
        {
            _transactionStarted = true;

            // A new transaction is a new try of the save, whose commit must be announced again.
            _announcedForTransaction = false;
        }
    }

    /// <summary>Records that one of the save's commands ran to the end.</summary>
    public void CommandSucceeded(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            _keys.UnionWith(keys);
            _commandSucceeded = true;
        }
    }

    /// <summary>
    /// Returns the tables for an announcement made because the save's own transaction committed, and none when
    /// that transaction's end was already announced: a commit that throws after it was applied reports its end
    /// twice.
    /// </summary>
    public string[] TakeForCommit()
    {
        lock (_lock)
        {
            if (_announcedForTransaction)
            {
                return [];
            }

            _announced = true;
            _announcedForTransaction = true;

            return [.. _keys];
        }
    }

    /// <summary>
    /// Returns the tables still to route when the save returned normally: none when its own transaction already
    /// announced them.
    /// </summary>
    public string[] TakeForSuccess()
    {
        lock (_lock)
        {
            return _announced ? [] : [.. _keys];
        }
    }

    /// <summary>
    /// Returns the tables that may be stored although the save threw or was cancelled: those of a save whose
    /// command ran outside a transaction of its own, so nothing rolled it back with the failure.
    /// </summary>
    public string[] TakeForFailure()
    {
        lock (_lock)
        {
            return _announced || _transactionStarted || !_commandSucceeded ? [] : [.. _keys];
        }
    }
}
