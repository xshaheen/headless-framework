// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Transactions;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Holds the tables and rows a tracked context wrote until the write is durable, then announces them to the hub. A
/// write is
/// held by whatever owns its commit: the unit of work bound to the connection, the EF Core transaction, or the
/// ambient <see cref="Transaction" />. A write outside all three is announced at once.
/// </summary>
/// <remarks>
/// Nothing here may fail or hold back the application's own save or commit, so every member catches what it throws,
/// counts it, and announces the tables it knows.
/// </remarks>
internal sealed partial class LiveWriteTracker(
    LiveQueryHub hub,
    LiveQueriesMetrics metrics,
    ILogger<LiveWriteTracker>? logger = null
)
{
    private readonly ILogger _logger = logger ?? NullLogger<LiveWriteTracker>.Instance;

    // Keyed on the transaction, not the context: a second context that adopted the transaction adds to the same
    // set, and a transaction that is disposed without a commit takes its set with it.
    private readonly ConditionalWeakTable<DbTransaction, LivePendingWrites> _transactions = [];

    // Only for the span of one SaveChanges call; removed when the save reports its end.
    private readonly ConditionalWeakTable<DbContext, LiveSaveCapture> _saves = [];

    private readonly Lock _ambientLock = new();
    private readonly Dictionary<Transaction, LivePendingWrites> _ambient = [];

    /// <summary>
    /// Signals the live queries <paramref name="writes" /> could change. The write must already be durable.
    /// </summary>
    /// <remarks>
    /// Runs on the caller's thread. The hub only stamps the keys and signals the subscriptions that read them; it
    /// runs no query and does no I/O, so handing the call to another thread would cost more than making it.
    /// </remarks>
    public void Announce(LiveWriteSet writes)
    {
        if (writes.IsEmpty)
        {
            return;
        }

        try
        {
            hub.Invalidate(writes);
        }
#pragma warning disable CA1031 // The fix would make it worse: an announcement that throws must not fail a commit that already succeeded.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Failed(exception);
        }
    }

    /// <summary>
    /// Records a write that has run and is durable once the transaction it ran in commits, or now when it ran in
    /// none.
    /// </summary>
    public void Written(DbContext context, IReadOnlyCollection<string> keys)
    {
        Written(context, new LiveWriteSet(keys, []));
    }

    /// <inheritdoc cref="Written(DbContext, IReadOnlyCollection{string})" />
    public void Written(DbContext context, LiveWriteSet writes)
    {
        if (writes.IsEmpty)
        {
            return;
        }

        try
        {
            if (_TryHold(context, writes))
            {
                return;
            }
        }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's write.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Failed(exception);
        }

        Announce(writes);
    }

    /// <summary>
    /// Returns whether a live query reads <paramref name="key" /> through row filters, so a save should keep the
    /// rows it writes to that table.
    /// </summary>
    public bool HasNarrowedReader(string key) => hub.HasNarrowedReader(key);

    /// <summary>Starts the capture of one save, replacing whatever an earlier save left on the context.</summary>
    public LiveSaveCapture BeginSave(DbContext context)
    {
        // A save whose start another interceptor refused never reports an end, and would go on watching the
        // change tracker.
        if (_saves.TryGetValue(context, out var abandoned))
        {
            abandoned.StopWatching();
        }

        var capture = new LiveSaveCapture(context.ContextId, Failed);
        _saves.AddOrUpdate(context, capture);

        return capture;
    }

    /// <summary>Records that the save running on <paramref name="context" /> is about to send a command.</summary>
    public void SaveCommandStarting(DbContext context)
    {
        _FindSave(context)?.StopWatching();
    }

    /// <summary>Records that a command of the save running on <paramref name="context" /> ran to the end.</summary>
    public void SaveCommandSucceeded(DbContext context, IReadOnlyCollection<string> keys)
    {
        if (_FindSave(context) is { } capture)
        {
            capture.CommandSucceeded(keys);
        }
        else
        {
            // A save command with no save in progress is still a write; route it like any other command.
            Written(context, keys);
        }
    }

    /// <summary>Ends the capture of a save that returned normally.</summary>
    public void SaveSucceeded(DbContext context)
    {
        if (_TakeSave(context) is { } capture)
        {
            Written(context, capture.TakeForSuccess());
        }
    }

    /// <summary>Ends the capture of a save that threw or was cancelled.</summary>
    public void SaveFailed(DbContext context)
    {
        if (_TakeSave(context) is { } capture)
        {
            Written(context, capture.TakeForFailure());
        }
    }

    /// <summary>Records that EF Core began a transaction on <paramref name="context" />.</summary>
    public void TransactionStarted(DbContext context)
    {
        // A transaction begun while a save is running is the one EF Core opens around that save.
        _FindSave(context)?.TransactionStarted();
    }

    /// <summary>Records how an EF Core transaction ended.</summary>
    /// <param name="context">The context whose transaction ended, when EF Core names it.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="mayBeDurable">
    /// Whether the writes may be stored: the transaction committed, or its commit failed with no answer.
    /// </param>
    /// <param name="inDoubt">
    /// Whether the commit failed with no answer. What it stored is then not known row by row, so the tables are
    /// announced whole.
    /// </param>
    public void TransactionEnded(DbContext? context, DbTransaction transaction, bool mayBeDurable, bool inDoubt = false)
    {
        var writes = LiveWriteSet.Empty;

        try
        {
            if (_transactions.TryGetValue(transaction, out var pending))
            {
                _transactions.Remove(transaction);
            }

            // A commit while a save is running ends the transaction EF Core opened for that save, so the save's
            // writes are durable now. A rollback leaves them: an execution strategy may run the save again.
            if (mayBeDurable && context is not null && _FindSave(context) is { } capture)
            {
                (pending ??= new LivePendingWrites()).Add(capture.TakeForCommit());
            }

            writes = pending?.Take() ?? writes;
        }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's commit.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Failed(exception);
        }

        if (mayBeDurable)
        {
            Announce(inDoubt ? writes.AsWholeKeys() : writes);
        }
    }

    /// <summary>Counts and logs a tracking step that threw and was contained.</summary>
    public void Failed(Exception exception)
    {
        metrics.TrackingFellBack();
        _LogTrackingFailed(_logger, exception);
    }

    private bool _TryHold(DbContext context, LiveWriteSet writes)
    {
        // The unit is looked up on the connection, which the owning context, a context that joined its transaction
        // and a raw-ADO unit all share.
        if (context.Database.GetDbConnection().UnitOfWork() is { } unit)
        {
            unit.GetOrAdd(this, static (unit, tracker) => new LiveUnitWrites(unit, tracker)).Add(writes);

            return true;
        }

        if (context.Database.CurrentTransaction is { } transaction)
        {
            _transactions.GetValue(transaction.GetDbTransaction(), static _ => new LivePendingWrites()).Add(writes);

            return true;
        }

        // EF Core's own rule for "this command runs in a transaction": one it was told to enlist in, or the ambient
        // one of a TransactionScope.
        if ((context.Database.GetEnlistedTransaction() ?? Transaction.Current) is { } ambient)
        {
            _HoldAmbient(ambient, writes);

            return true;
        }

        return false;
    }

    private void _HoldAmbient(Transaction transaction, LiveWriteSet writes)
    {
        var created = false;

        lock (_ambientLock)
        {
            if (!_ambient.TryGetValue(transaction, out var pending))
            {
                pending = new LivePendingWrites();
                _ambient.Add(transaction, pending);
                created = true;
            }

            pending.Add(writes);
        }

        if (!created)
        {
            return;
        }

        try
        {
            // Raised at once when the transaction has already ended, so a late registration still announces.
            transaction.TransactionCompleted += (_, completed) => _AmbientCompleted(transaction, completed);
        }
        catch
        {
            // Nothing will ever report this transaction's end, so its entry must not stay; the caller announces
            // the tables now instead.
            lock (_ambientLock)
            {
                _ambient.Remove(transaction);
            }

            throw;
        }
    }

    private void _AmbientCompleted(Transaction key, TransactionEventArgs completed)
    {
        LivePendingWrites? pending;

        lock (_ambientLock)
        {
            _ambient.Remove(key, out pending);
        }

        if (pending is null)
        {
            return;
        }

        // Unknown until read, and unknown when reading it throws.
        var status = TransactionStatus.InDoubt;

        try
        {
            status = completed.Transaction?.TransactionInformation.Status ?? status;
        }
#pragma warning disable CA1031 // The fix would make it worse: an exception here would surface from the application's TransactionScope.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Failed(exception);
        }

        // Committed and in-doubt both announce; only a transaction known to have aborted stored nothing. In doubt,
        // what was stored is not known row by row, so the tables are announced whole.
        if (status is not TransactionStatus.Aborted)
        {
            var writes = pending.Take();
            Announce(status is TransactionStatus.Committed ? writes : writes.AsWholeKeys());
        }
    }

    private LiveSaveCapture? _FindSave(DbContext context)
    {
        return _saves.TryGetValue(context, out var capture) && capture.ContextId == context.ContextId ? capture : null;
    }

    private LiveSaveCapture? _TakeSave(DbContext context)
    {
        var capture = _FindSave(context);
        _saves.Remove(context);

        return capture;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "LiveQueriesTrackingFailed",
        Level = LogLevel.Error,
        Message = "Live-query change tracking failed and fell back to announcing the tables it knew; a live query may miss this write"
    )]
    private static partial void _LogTrackingFailed(ILogger logger, Exception exception);
}
