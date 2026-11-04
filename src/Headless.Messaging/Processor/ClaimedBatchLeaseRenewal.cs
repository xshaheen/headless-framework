// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

/// <summary>
/// Keeps the claim lease of retry rows that wait their turn in a serially dispatched pickup batch.
/// </summary>
/// <remarks>
/// <para>
/// One pickup claims a whole batch for <see cref="RetryPolicyOptions.DispatchTimeout"/>, and the batch is dispatched one
/// row after another. Each attempt's reservation re-stamps its own row's lease, but only while the claim is still live,
/// so without renewal a row reached after the claim ran out is skipped and released, and the batch behind a slow or
/// rate-limited consumer churns through claims instead of running. This renews every row still waiting on the
/// policy's renewal cadence, until the row is taken for dispatch.
/// </para>
/// <para>
/// A row is renewed and taken under one gate, so its lease identity never changes while its dispatch reads it. A
/// renewal that finds the lease gone drops the row: its reservation then fails and the row is released as before. A
/// renewal that throws is logged and tried again on the next tick; a store that cannot renew stops the loop and the
/// rows keep the lease their claim stamped.
/// </para>
/// <para>
/// The cadence runs on the app clock, so a test advances it like any other schedule; the bound on each renewal call
/// runs on the back-off clock; the lease itself is stamped by the store's clock.
/// </para>
/// </remarks>
internal sealed class ClaimedBatchLeaseRenewal : IAsyncDisposable
{
    private readonly HashSet<MediumMessage> _waiting;
    private readonly Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> _renew;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _clock;
    private readonly TimeProvider _backoffClock;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;
    private int _stopped;

    private ClaimedBatchLeaseRenewal(
        IEnumerable<MediumMessage> messages,
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> renew,
        RetryPolicyOptions policy,
        TimeProvider clock,
        TimeProvider backoffClock,
        ILogger logger
    )
    {
        _waiting = new HashSet<MediumMessage>(messages, ReferenceEqualityComparer.Instance);
        _renew = renew;
        _leaseDuration = policy.DispatchTimeout;
        _interval = policy.ResolveLeaseRenewalInterval();
        _clock = clock;
        _backoffClock = backoffClock;
        _logger = logger;
        _loop = _waiting.Count == 0 ? Task.CompletedTask : _RenewLoopAsync();
    }

    /// <summary>Starts renewing the claim lease of every row in <paramref name="messages"/>.</summary>
    public static ClaimedBatchLeaseRenewal Start(
        IEnumerable<MediumMessage> messages,
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> renew,
        RetryPolicyOptions policy,
        TimeProvider clock,
        TimeProvider backoffClock,
        ILogger logger
    )
    {
        return new ClaimedBatchLeaseRenewal(messages, renew, policy, clock, backoffClock, logger);
    }

    /// <summary>
    /// Stops renewing <paramref name="message"/> before its dispatch, waiting for a renewal pass in flight so the
    /// message's lease identity is settled before the dispatch reads it.
    /// </summary>
    public async ValueTask TakeAsync(MediumMessage message)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _waiting.Remove(message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops renewing every row and waits for a renewal pass in flight, so each remaining row's lease identity is
    /// settled before it is released or disposed. Safe to call more than once.
    /// </summary>
    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
        }

        await _loop.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stopping.Dispose();
        _gate.Dispose();
    }

    private async Task _RenewLoopAsync()
    {
        while (true)
        {
            try
            {
                await Task.Delay(_interval, _clock, _stopping.Token).ConfigureAwait(false);
                await _gate.WaitAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                // A pass is not cancelled by the stop signal: a renewal that committed but was cancelled before it
                // returned would leave the message holding the old lease identity. Each call is bounded instead.
                foreach (var message in _waiting.ToArray())
                {
                    bool renewed;
                    try
                    {
                        using var timeout = new CancellationTokenSource(_interval, _backoffClock);
                        renewed = await _renew(message, _leaseDuration, timeout.Token).ConfigureAwait(false);
                    }
                    catch (NotSupportedException)
                    {
                        // The store cannot renew; the rows keep the lease their claim stamped.
                        return;
                    }
                    catch (Exception exception)
                    {
                        // Says nothing about the lease; the next tick tries again while the claim may still be live.
                        _logger.ClaimedRetryLeaseRenewalFailed(exception, message.StorageId);
                        continue;
                    }

                    if (!renewed)
                    {
                        _waiting.Remove(message);
                    }
                }

                if (_waiting.Count == 0)
                {
                    return;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
