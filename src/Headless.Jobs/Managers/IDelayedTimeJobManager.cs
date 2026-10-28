// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Managers;

/// <summary>
/// Enqueues time jobs whose due time is a delay after the store's clock at write time, rather than an instant the
/// caller computes from its own clock.
/// </summary>
/// <remarks>
/// Kept off the public <see cref="ITimeJobManager{TTimeJob}"/>: the delay is an intent the store resolves inside the
/// write, not an entity field, so schedule middleware and custom providers see a delayed job exactly as a run-now job
/// (<c>ExecutionTime</c> null). A middleware that sets an absolute <c>ExecutionTime</c> wins over the delay.
/// </remarks>
internal interface IDelayedTimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    /// <summary>Enqueues <paramref name="entity"/> due <paramref name="delay"/> after the store's instant at write.</summary>
    Task<TTimeJob> AddAfterAsync(TTimeJob entity, TimeSpan delay, CancellationToken cancellationToken);

    /// <summary>
    /// The idempotent form of <see cref="AddAfterAsync"/>: a creation is due <paramref name="delay"/> after the store's
    /// instant at write; a hit inside the window writes nothing.
    /// </summary>
    Task<TTimeJob> AddIdempotentAfterAsync(
        TTimeJob entity,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        TimeSpan delay,
        CancellationToken cancellationToken
    );
}
