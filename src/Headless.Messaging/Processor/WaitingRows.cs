// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

/// <summary>
/// Keeps rows whose delivery is alive on this host but cannot run yet out of retry pickup: it puts off the pickup of
/// an unleased row, and renews the claim a waiting retry already holds. A row waits so behind its key or for a slot of
/// a sequential consumer, or for its consumer's rate-limit permit.
/// </summary>
/// <remarks>
/// A row is first held on the second pass that finds it waiting, so a message that runs within a tick costs no
/// write. With a tick of a quarter of the grace, a first dispatch is put off before half its grace has passed, by
/// one whole grace, and again every second pass, so its pickup always stays at least half a grace away. A row is
/// held, and taken for dispatch, under one gate, so its lease identity never changes while its dispatch reads it.
/// The cadence runs on the app clock; the bound on each call runs on the back-off clock; the due time and the
/// lease are stamped by the store's clock.
/// </remarks>
#pragma warning disable CA1001 // Disposing would make the code worse: a runner may take a message after the stop and would meet a disposed gate, and neither field holds a handle unless its wait handle is read.
internal sealed class WaitingRows
#pragma warning restore CA1001
{
    private const int _PassesPerDeferral = 2;

    private readonly HashSet<WaitingRow> _waiting = new(ReferenceEqualityComparer.Instance);
    private readonly Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> _defer;
    private readonly Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> _renew;
    private readonly TimeSpan _grace;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _tick;
    private readonly int _passesPerRenewal;
    private readonly TimeProvider _clock;
    private readonly TimeProvider _backoffClock;
    private readonly ILogger _logger;
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;
    private bool _stopped;
    private bool _cannotDefer;
    private bool _cannotRenew;

    public WaitingRows(
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> defer,
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> renew,
        RetryPolicyOptions policy,
        TimeProvider clock,
        TimeProvider backoffClock,
        ILogger logger
    )
    {
        _defer = defer;
        _renew = renew;
        _grace = policy.InitialDispatchGrace;
        _leaseDuration = policy.DispatchTimeout;
        var renewalInterval = policy.ResolveLeaseRenewalInterval();
        var quarterGrace = TimeSpan.FromTicks(Math.Max(1, _grace.Ticks / 4));
        _tick = quarterGrace < renewalInterval ? quarterGrace : renewalInterval;
        _passesPerRenewal = (int)Math.Max(1, renewalInterval.Ticks / _tick.Ticks);
        _clock = clock;
        _backoffClock = backoffClock;
        _logger = logger;
    }

    /// <summary>Starts watching a message that could not run at once.</summary>
    public void Add(WaitingRow work)
    {
        // A retry pickup arrives holding a claim of unknown age, so its first renewal comes at half the cadence.
        work.PassesUntilHold =
            work.Message.Lease.State is RowLeaseState.Active ? Math.Max(1, _passesPerRenewal / 2) : 0;

        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }

            _waiting.Add(work);
            _loop ??= Task.Run(_LoopAsync);
        }
    }

    /// <summary>
    /// Stops watching <paramref name="work"/> before its dispatch, waiting out a store call in flight for it.
    /// </summary>
    public async ValueTask TakeAsync(WaitingRow work)
    {
        lock (_sync)
        {
            if (!_waiting.Remove(work))
            {
                return;
            }
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    /// <summary>Stops the passes and waits for a store call in flight. Safe to call more than once.</summary>
    public async Task StopAsync()
    {
        Task? loop;
        lock (_sync)
        {
            loop = _stopped ? null : _loop;
            _stopped = true;
            _waiting.Clear();
        }

        if (loop is null)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await loop.ConfigureAwait(false);
    }

    private async Task _LoopAsync()
    {
        while (true)
        {
            // Ends once no row waits, and the next Add starts it again: a loop that kept ticking would arm a timer every
            // tick for nothing until the host disposed it, past the host's stop, so a stopping host would never be
            // idle on its clock.
            lock (_sync)
            {
                if (_waiting.Count == 0)
                {
                    _loop = null;
                    return;
                }
            }

            WaitingRow[] snapshot;
            try
            {
                await Task.Delay(_tick, _clock, _stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_sync)
            {
                snapshot = [.. _waiting];
            }

            foreach (var work in snapshot)
            {
                try
                {
                    await _gate.WaitAsync(_stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    // A call is not cancelled by the stop: a renewal that committed but was cancelled before it
                    // returned would leave the message holding the old lease identity. Each call is bounded.
                    await _HoldAsync(work).ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
    }

    /// <summary>Puts off the pickup of one waiting first dispatch, or renews the claim of one waiting retry.</summary>
    private async ValueTask _HoldAsync(WaitingRow work)
    {
        lock (_sync)
        {
            if (!_waiting.Contains(work))
            {
                return;
            }
        }

        var message = work.Message;
        var claimed = message.Lease.State is RowLeaseState.Active;
        if (claimed ? _cannotRenew : _cannotDefer)
        {
            return;
        }

        if (!claimed && !work.SeenWaiting)
        {
            work.SeenWaiting = true;
            return;
        }

        if (--work.PassesUntilHold > 0)
        {
            return;
        }

        bool held;
        try
        {
            using var timeout = new CancellationTokenSource(_tick, _backoffClock);
            held = claimed
                ? await _renew(message, _leaseDuration, timeout.Token).ConfigureAwait(false)
                : await _defer(message, _grace, timeout.Token).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // The store cannot hold such a row; these rows are left to the retry pickup.
            if (claimed)
            {
                _cannotRenew = true;
            }
            else
            {
                _cannotDefer = true;
            }

            return;
        }
#pragma warning disable CA1031 // Says nothing about the row: the next pass tries again while the grace or the claim may still hold.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.WaitingRowHoldFailed(exception, message.StorageId);
            return;
        }

        if (held)
        {
            work.PassesUntilHold = claimed ? _passesPerRenewal : _PassesPerDeferral;
        }
        else
        {
            // The row is terminal, gone, or held by another attempt; its dispatch finds that out and writes nothing.
            lock (_sync)
            {
                _waiting.Remove(work);
            }
        }
    }
}
