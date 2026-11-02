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
/// EF Core's guidance for a user transaction under a retrying strategy is a new context per attempt. The block runs
/// on the caller's context, so the closest equivalent is a context with nothing tracked: when the first attempt
/// began on an empty tracker, every replay clears it again, and the block re-reads its rows from the rolled-back
/// database. Deferring <c>AcceptAllChanges</c> to the commit is not enough, because a block that saves twice would
/// insert the first save's rows again, and in-memory edits would still survive. A tracker that held entities before
/// the block cannot be restored: clearing it would drop the caller's pending changes, and the block may have edited
/// those entities. Its fault is therefore marked non-replayable and surfaces to the caller.
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

        // A replay is reached only from an attempt that began on an empty tracker; any other attempt's fault was
        // marked non-replayable below.
        db.ChangeTracker.Clear();
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
            if (_attempts > 0 && !_startedEmpty)
            {
                unitOfWork.PreventRetry();
            }

            throw;
        }
    }
}
