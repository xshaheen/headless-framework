// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Headless.Messaging.Internal;

/// <summary>
/// The host's rate limiters, one per rate-limited consumer identity, created on the identity's first delivery and
/// disposed with the host.
/// </summary>
/// <remarks>
/// A consumer identity covers several messages, and on some hosts both lanes, so the limiter is keyed by identity
/// rather than by descriptor: every delivery of the consumer, whatever its message or lane, draws from one rate.
/// </remarks>
internal sealed class ConsumerRateLimiters : IDisposable
{
    private readonly ConcurrentDictionary<string, RateLimiter> _limiters = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();
    private bool _disposed;

    /// <summary>
    /// Waits until <paramref name="consumer"/> may start one more delivery. Returns at once for a consumer with no rate
    /// limit.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled while waiting, or the host disposed the limiter while it
    /// stopped.
    /// </exception>
    public ValueTask WaitAsync(ConsumerExecutorDescriptor consumer, CancellationToken cancellationToken)
    {
        if (consumer.RateLimit is not { } rateLimit)
        {
            return ValueTask.CompletedTask;
        }

        return _GetLimiter(consumer.ResolvedConsumerIdentity, rateLimit) is { } limiter
            ? _WaitAsync(limiter, cancellationToken)
            : ValueTask.FromException(_Disposed(innerException: null, cancellationToken));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }

        _limiters.Clear();
    }

    private static async ValueTask _WaitAsync(RateLimiter limiter, CancellationToken cancellationToken)
    {
        RateLimitLease lease;
        try
        {
            lease = await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException exception)
        {
            throw _Disposed(exception, cancellationToken);
        }

        // A permit spends itself: fixed-window and token-bucket leases hold nothing, so it is released at once.
        using (lease)
        {
            if (!lease.IsAcquired)
            {
                // The queue has no practical bound, so only disposal fails a waiting acquisition.
                throw _Disposed(innerException: null, cancellationToken);
            }
        }
    }

    // The host disposes its limiters only once it is stopping, so a delivery that reaches a disposed limiter is being
    // shut down, not failing: it surfaces as a cancellation, like the stop that the delivery's own token reports.
    private static OperationCanceledException _Disposed(
        Exception? innerException,
        CancellationToken cancellationToken
    ) =>
        new(
            "The consumer's rate limiter was disposed because the host is stopping.",
            innerException,
            cancellationToken
        );

    private RateLimiter? _GetLimiter(string identity, ConsumerRateLimit rateLimit)
    {
        if (_limiters.TryGetValue(identity, out var limiter))
        {
            return limiter;
        }

        // Created under the lock so concurrent first deliveries cannot each build a limiter and leak the losers' timers.
        lock (_sync)
        {
            if (_disposed)
            {
                return null;
            }

            if (!_limiters.TryGetValue(identity, out limiter))
            {
                limiter = rateLimit.CreateLimiter();
                _limiters[identity] = limiter;
            }

            return limiter;
        }
    }
}
