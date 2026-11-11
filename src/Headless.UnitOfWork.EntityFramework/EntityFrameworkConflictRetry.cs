// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;

namespace Headless.UnitOfWork;

/// <summary>
/// The replay policy for a <c>RunAsync(db, …, retry, …)</c> block that reads rows, decides, and saves them under an
/// optimistic concurrency token: when another writer changed a row first, the save throws
/// <see cref="DbUpdateConcurrencyException" />, and the block runs again on fresh rows instead of failing the caller.
/// </summary>
/// <remarks>
/// Pass <see cref="Create" /> as the call's <c>retry:</c> argument, or build a <see cref="RetryStrategyOptions" /> of
/// your own with <see cref="DefaultShouldHandle" />. A replay repeats every effect of the block that is not in the
/// unit's transaction, so keep those effects inside it (<c>unit.Outbox</c>, <c>unit.Jobs</c>).
/// </remarks>
[PublicAPI]
public static class EntityFrameworkConflictRetry
{
    private const int _DefaultMaxAttempts = 3;
    private static readonly TimeSpan _Delay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan _MaxDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets the conflict classification: replay a lost optimistic-concurrency check
    /// (<see cref="DbUpdateConcurrencyException" />) and nothing else. Reuse (or compose) this predicate when supplying
    /// a <see cref="RetryStrategyOptions" /> of your own; Polly's own default replays every exception that is not a
    /// cancellation.
    /// </summary>
    public static Func<RetryPredicateArguments<object>, ValueTask<bool>> DefaultShouldHandle { get; } =
        static args => ValueTask.FromResult(args.Outcome.Exception is DbUpdateConcurrencyException);

    /// <summary>
    /// Creates a policy that replays a block that lost an optimistic-concurrency check, running it at most
    /// <paramref name="maxAttempts" /> times in total with a short, jittered, exponential delay between attempts, so
    /// writers that collided once do not collide again in step.
    /// </summary>
    /// <param name="maxAttempts">
    /// The most times the block runs, the first run included. A block still in conflict on its last run surfaces the
    /// <see cref="DbUpdateConcurrencyException" />. Set it to at least the number of writers expected to contend for
    /// one row when every one of them must succeed.
    /// </param>
    /// <returns>A new options instance the caller may adjust before passing it as <c>retry:</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts" /> is less than two.</exception>
    public static RetryStrategyOptions Create(int maxAttempts = _DefaultMaxAttempts)
    {
        Argument.IsGreaterThanOrEqualTo(maxAttempts, 2);

        return new RetryStrategyOptions
        {
            ShouldHandle = DefaultShouldHandle,
            MaxRetryAttempts = maxAttempts - 1,
            BackoffType = DelayBackoffType.Exponential,
            Delay = _Delay,
            MaxDelay = _MaxDelay,
            UseJitter = true,
        };
    }
}
