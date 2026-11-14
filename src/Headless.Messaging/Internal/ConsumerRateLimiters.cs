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
    private readonly ConcurrentDictionary<string, ConsumerLimiter> _limiters = new(StringComparer.Ordinal);

    // The stored rows whose delivery is waiting for a permit on this host, with how many deliveries wait for each.
    private readonly Dictionary<Guid, int> _waitingRows = [];
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
            ? _WaitAsync(limiter.Limiter, cancellationToken)
            : ValueTask.FromException(_Disposed(innerException: null, cancellationToken));
    }

    /// <summary>
    /// Waits as <see cref="WaitAsync(ConsumerExecutorDescriptor, CancellationToken)"/> does, for the delivery of the
    /// stored row <paramref name="storageId"/>. While it waits, <see cref="IsWaiting"/> reports the row, so the retry
    /// processor leaves it to this delivery instead of dispatching it a second time.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled while waiting, or the host disposed the limiter while it
    /// stopped.
    /// </exception>
    public ValueTask WaitAsync(ConsumerExecutorDescriptor consumer, Guid storageId, CancellationToken cancellationToken)
    {
        var wait = WaitAsync(consumer, cancellationToken);

        // The common case holds a permit at once and never shows as waiting.
        return wait.IsCompletedSuccessfully ? wait : _WaitAsRowAsync(wait, storageId);
    }

    /// <summary>
    /// Whether a delivery of the stored row <paramref name="storageId"/> is waiting for a permit on this host. Such a
    /// row holds no lease, so it looks abandoned to a retry pickup once its dispatch grace ends, while its delivery is
    /// still alive and will run it.
    /// </summary>
    public bool IsWaiting(Guid storageId)
    {
        lock (_sync)
        {
            return _waitingRows.ContainsKey(storageId);
        }
    }

    /// <summary>
    /// Takes one permit for <paramref name="consumer"/> without waiting. Returns <see langword="true"/> for a consumer
    /// with no rate limit.
    /// </summary>
    /// <remarks>
    /// A caller that cannot wait has no place in the limiter's queue, and the limiter grants nothing out of turn while
    /// deliveries wait in it, so under steady traffic such a caller would never get a permit. A refused call therefore
    /// leaves one acquisition queued for the consumer, and a later call takes the permit that acquisition was granted.
    /// That permit is held until a call takes it, so every permit granted still starts one delivery.
    /// </remarks>
    public bool TryAcquire(ConsumerExecutorDescriptor consumer)
    {
        if (consumer.RateLimit is not { } rateLimit)
        {
            return true;
        }

        return _GetLimiter(consumer.ResolvedConsumerIdentity, rateLimit)?.TryAcquire() ?? false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        foreach (var limiter in _limiters.Values)
        {
            limiter.Limiter.Dispose();
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

    private async ValueTask _WaitAsRowAsync(ValueTask wait, Guid storageId)
    {
        lock (_sync)
        {
            _waitingRows[storageId] = _waitingRows.GetValueOrDefault(storageId) + 1;
        }

        try
        {
            await wait.ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                var waiting = _waitingRows[storageId] - 1;
                if (waiting == 0)
                {
                    _waitingRows.Remove(storageId);
                }
                else
                {
                    _waitingRows[storageId] = waiting;
                }
            }
        }
    }

    private ConsumerLimiter? _GetLimiter(string identity, ConsumerRateLimit rateLimit)
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
                limiter = new ConsumerLimiter(rateLimit.CreateLimiter());
                _limiters[identity] = limiter;
            }

            return limiter;
        }
    }

    /// <summary>One consumer's limiter, with the acquisition queued for callers that cannot wait.</summary>
    private sealed class ConsumerLimiter(RateLimiter limiter)
    {
        private readonly Lock _sync = new();
        private Task<RateLimitLease>? _queued;

        public RateLimiter Limiter { get; } = limiter;

        public bool TryAcquire()
        {
            lock (_sync)
            {
                if (_queued is { IsCompleted: false })
                {
                    return false;
                }

                try
                {
                    if (_queued is { } granted)
                    {
                        _queued = null;

                        // A permit spends itself, as in the waiting path, so each lease is released at once.
                        using var queuedLease = granted.Result;
                        if (queuedLease.IsAcquired)
                        {
                            return true;
                        }
                    }

                    using var lease = Limiter.AttemptAcquire();
                    if (lease.IsAcquired)
                    {
                        return true;
                    }

                    // No cancellation: the acquisition outlives this call, and ends with the limiter's disposal.
                    _queued = Limiter.AcquireAsync(1, CancellationToken.None).AsTask();
                }
                catch (ObjectDisposedException)
                {
                    // The host is stopping; nothing more is dispatched.
                }

                return false;
            }
        }
    }
}
