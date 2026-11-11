// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// Keeps a replayed <c>RunAsync(db, …)</c> block from running over the change tracker its failed attempt left
/// behind. The rollback undoes the attempt's rows, but not the tracker: a save that succeeded inside the attempt
/// already accepted its changes, and every entity the block edited keeps the edit in memory. A replay over that
/// tracker reapplies a relative update to the edited value and skips an absolute one as already saved.
/// </summary>
/// <remarks>
/// A block replays under a retrying execution strategy, under a retry policy the call passed (a lost
/// optimistic-concurrency check), or both; the guard is the same for each, and is inert when neither can replay.
/// EF Core's guidance for a user transaction under a retrying strategy is a new context per attempt. The block runs
/// on the caller's context, so the closest equivalent is a context with nothing tracked: when the first attempt
/// began on an empty tracker, every replay clears it again, and the block re-reads its rows from the rolled-back
/// database. Deferring <c>AcceptAllChanges</c> to the commit is not enough, because a block that saves twice would
/// insert the first save's rows again, and in-memory edits would still survive. A tracker that held entities before
/// the block cannot be restored: clearing it would drop the caller's pending changes, and the block may have edited
/// those entities. Its fault is therefore marked non-replayable and surfaces to the caller, and a retry of a fault
/// raised before the block ran (the connection open or the transaction begin) leaves that tracker as it is. The
/// same holds for a second context that joined the unit over the shared connection and still tracks an entity when the attempt
/// fails: the guard owns only the context the block was run on, and clearing another context's tracker could drop
/// changes its own caller is still holding. A second context that has not joined yet is unknown to the unit, so
/// what it tracked before its first lookup in the attempt does not stop a replay; the block has to track through
/// it only after it joined.
/// </remarks>
internal sealed class DbContextReplayGuard(DbContext db, bool replays)
{
    private int _attempts;
    private bool _startedEmpty;

    /// <summary>Runs before each owned attempt begins its unit.</summary>
    public void BeforeAttempt()
    {
        if (!replays)
        {
            return;
        }

        if (++_attempts == 1)
        {
            _startedEmpty = !db.ChangeTracker.Entries().Any();

            return;
        }

        // A later attempt is not always a replay of the block: the strategy also retries a fault raised before
        // the block ran, such as opening the connection or beginning the transaction. On a tracker that held
        // entities before the first attempt, that is the only way to get here (a block fault was marked
        // non-replayable below), and those entities are the caller's pending changes, so they stay.
        if (_startedEmpty)
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Runs the block, marking its fault non-replayable when the tracker cannot be restored.</summary>
    public async Task<TResult> RunAsync<TResult>(
        IUnitOfWork unitOfWork,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await operation(unitOfWork, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // No attempt began for a block that joined a live unit: its owner's block decides any replay.
            if (
                _attempts > 0
                && (!_startedEmpty || DbContextUnitOfWorkBinding.HasJoinedContextWithTrackedEntities(unitOfWork))
            )
            {
                unitOfWork.PreventRetry();
            }

            throw;
        }
    }
}
