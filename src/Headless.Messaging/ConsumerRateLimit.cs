// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.RateLimiting;
using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// How fast one consumer may start handling deliveries on this host, set through
/// <see cref="ConsumerTuningBuilder.RateLimit"/>. A delivery over the rate waits for a permit before its consume
/// middleware and handler run; it is never dropped or rejected.
/// </summary>
/// <remarks>
/// The limit is local to one process: each host that runs the consumer allows the full rate, so the rate a fleet sees
/// is this rate times the number of hosts. Every message and lane of one consumer identity draws from one shared
/// limiter, which measures real time.
/// </remarks>
[PublicAPI]
public sealed class ConsumerRateLimit
{
    // A full queue fails the acquisition instead of waiting, which would turn throttling into a failed delivery. Only
    // deliveries already in flight on this host can wait, so the queue needs no real bound; the largest one means an
    // acquisition always waits.
    private const int _QueueLimit = int.MaxValue;

    /// <summary>The longest window or replenishment period a rate limit accepts.</summary>
    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(1);

    private readonly Func<RateLimiter> _createLimiter;

    private ConsumerRateLimit(Func<RateLimiter> createLimiter)
    {
        _createLimiter = createLimiter;
    }

    /// <summary>
    /// Allows <paramref name="permitLimit"/> deliveries in each fixed <paramref name="window"/>. A burst of up to
    /// <paramref name="permitLimit"/> deliveries can start at the beginning of each window.
    /// </summary>
    /// <param name="permitLimit">The deliveries allowed per window; must be greater than zero.</param>
    /// <param name="window">The window length; greater than zero and no longer than <see cref="MaxPeriod"/>.</param>
    /// <returns>The rate limit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="permitLimit"/> is not positive, or <paramref name="window"/> is not positive or is longer than
    /// <see cref="MaxPeriod"/>.
    /// </exception>
    public static ConsumerRateLimit FixedWindow(int permitLimit, TimeSpan window)
    {
        Argument.IsPositive(permitLimit);
        Argument.IsLeftOpenedBetween(window, TimeSpan.Zero, MaxPeriod);

        return new ConsumerRateLimit(() =>
            new FixedWindowRateLimiter(
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = _QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }
            )
        );
    }

    /// <summary>
    /// Adds <paramref name="tokensPerPeriod"/> permits every <paramref name="replenishmentPeriod"/>, holding at most
    /// <paramref name="tokenLimit"/>. The steady rate is <paramref name="tokensPerPeriod"/> per period, and an idle
    /// consumer saves up a burst of at most <paramref name="tokenLimit"/> deliveries.
    /// </summary>
    /// <param name="tokenLimit">The most permits the bucket holds, and so the largest burst; greater than zero.</param>
    /// <param name="tokensPerPeriod">The permits added each period; greater than zero.</param>
    /// <param name="replenishmentPeriod">
    /// How often permits are added; greater than zero and no longer than <see cref="MaxPeriod"/>.
    /// </param>
    /// <returns>The rate limit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tokenLimit"/> or <paramref name="tokensPerPeriod"/> is not positive, or
    /// <paramref name="replenishmentPeriod"/> is not positive or is longer than <see cref="MaxPeriod"/>.
    /// </exception>
    public static ConsumerRateLimit TokenBucket(int tokenLimit, int tokensPerPeriod, TimeSpan replenishmentPeriod)
    {
        Argument.IsPositive(tokenLimit);
        Argument.IsPositive(tokensPerPeriod);
        Argument.IsLeftOpenedBetween(replenishmentPeriod, TimeSpan.Zero, MaxPeriod);

        return new ConsumerRateLimit(() =>
            new TokenBucketRateLimiter(
                new TokenBucketRateLimiterOptions
                {
                    TokenLimit = tokenLimit,
                    TokensPerPeriod = tokensPerPeriod,
                    ReplenishmentPeriod = replenishmentPeriod,
                    QueueLimit = _QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }
            )
        );
    }

    /// <summary>Creates the limiter one host shares across every delivery of the consumer.</summary>
    internal RateLimiter CreateLimiter() => _createLimiter();
}
