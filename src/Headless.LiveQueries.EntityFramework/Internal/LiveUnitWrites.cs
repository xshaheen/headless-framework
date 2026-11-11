// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The tables and rows written inside one unit of work, kept as that unit's own state so every context and
/// connection the unit covers adds to the same set and nothing outlives the unit.
/// </summary>
internal sealed class LiveUnitWrites
{
    private readonly LivePendingWrites _pending = new();

    /// <summary>Creates the state and asks the unit to report how it ends.</summary>
    /// <param name="unit">The unit that owns the commit.</param>
    /// <param name="tracker">Announces the writes once the unit's outcome is known.</param>
    public LiveUnitWrites(IUnitOfWork unit, LiveWriteTracker tracker)
    {
        unit.OnCompleted(() =>
        {
            tracker.Announce(_pending.Take());

            return ValueTask.CompletedTask;
        });

        unit.OnFailed(failure =>
        {
            var writes = _pending.Take();

            // An in-doubt commit may have stored the writes, or some of them, so the tables are announced whole;
            // every other failure is known to have stored nothing.
            if (failure.Reason is UnitOfWorkFailureReason.InDoubt)
            {
                tracker.Announce(writes.AsWholeKeys());
            }

            return ValueTask.CompletedTask;
        });
    }

    public void Add(LiveWriteSet writes) => _pending.Add(writes);
}
