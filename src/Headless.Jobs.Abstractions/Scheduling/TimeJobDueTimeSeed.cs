// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Resolves one root time job's due time from the instant the STORE reported inside the inserting write.
/// </summary>
/// <remarks>
/// A job enqueued to run now, or after a delay, has no absolute due time until it is written. Stamping it from the
/// enqueuing node's clock would shift it by that node's skew from the database, so a node minutes ahead would write a
/// row the store does not consider due yet. The provider therefore calls back with its own clock and the manager,
/// which knows which roots are relative and by how much, returns the due time. Mirrors
/// <see cref="CronSchedulePositionSeeder"/>.
/// </remarks>
/// <param name="root">A root job being inserted. Descendants are never passed: they wait for their parent.</param>
/// <param name="storeUtcNow">The store's instant, read once for the whole write.</param>
/// <returns>
/// The due time to persist on <paramref name="root"/>, or <see langword="null"/> to keep the
/// <c>ExecutionTime</c> it already carries (an absolute schedule).
/// </returns>
[PublicAPI]
public delegate DateTime? TimeJobDueTimeSeeder(BaseJobEntity root, DateTime storeUtcNow);

/// <summary>What a seeded time-job insert persisted: the store anchor it used and the rows it wrote.</summary>
/// <remarks>
/// <see cref="StoreUtcNow"/> reports the store anchor the write used, so a caller or test can see which instant the
/// store resolved due times against.
/// </remarks>
[PublicAPI]
public readonly record struct TimeJobDueTimeSeedResult
{
    /// <summary>
    /// The store instant every seeded root was resolved against, or <see langword="null"/> when nothing was inserted.
    /// </summary>
    public DateTime? StoreUtcNow { get; init; }

    /// <summary>Rows written, counting inserted children as well as roots.</summary>
    public int AffectedRows { get; init; }
}
