// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Fencing;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

/// <summary>
/// The dispatch stage of sequential consumers: the messages that share a routing affinity key run one at a time, in
/// the order they were handed over, and different keys run side by side up to the consumer's concurrency.
/// </summary>
/// <remarks>
/// <para>
/// One sequencer serves each consumer identity, so every message type and lane of the identity is ordered against the
/// others. A message without a key is a group of its own: it is ordered against nothing and still takes a concurrency
/// slot. After each message, a key with more work goes to the back of the line when other keys wait for a slot, so a
/// busy key cannot starve the rest.
/// </para>
/// <para>
/// A sequencer holds a bounded number of messages, running ones included. A hand-over to a full one waits, which holds
/// the consumer's single transport slot and stops intake until a message finishes.
/// </para>
/// <para>
/// A message is already stored and acknowledged when it is handed over, and its row becomes eligible for retry pickup
/// once its initial dispatch grace runs out. While a row waits behind its key its pickup is therefore put off again
/// and again, and a waiting retry's claim is renewed, so no host picks the row up out of order. A store that can do
/// neither leaves the row to that pickup, as after a crash.
/// </para>
/// </remarks>
internal sealed class ConsumerKeySequencers
{
    // Enough room behind each running key for a burst to queue without stopping intake, small enough that a stalled
    // consumer holds a bounded number of stored-but-unhandled rows in memory.
    private const int _BufferPerSlot = 8;
    private const int _MinimumBuffer = 32;

    private readonly ConcurrentDictionary<string, Sequencer> _sequencers = new(StringComparer.Ordinal);
    private readonly Func<SequencedWork, Task> _run;
    private readonly WaitingRows _waiting;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _sync = new();
    private int _running;
    private volatile bool _stopped;

    /// <summary>Creates the sequencers of one dispatcher start.</summary>
    /// <param name="run">Runs one message to the end of its dispatch, immediate retries included; it never throws.</param>
    /// <param name="defer">
    /// Puts off retry pickup of a waiting row; <see cref="Persistence.IDataStorage.DeferReceivedDispatchAsync"/>.
    /// </param>
    /// <param name="renew">
    /// Renews the claim of a waiting retry; <see cref="Persistence.IDataStorage.RenewReceiveLeaseAsync"/>.
    /// </param>
    /// <param name="policy">The retry policy that sets the pickup grace, the lease length, and the renewal cadence.</param>
    /// <param name="clock">The app clock the keeper's cadence runs on.</param>
    /// <param name="backoffClock">The clock that bounds each store call.</param>
    /// <param name="logger">The logger.</param>
    public ConsumerKeySequencers(
        Func<SequencedWork, Task> run,
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> defer,
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> renew,
        RetryPolicyOptions policy,
        TimeProvider clock,
        TimeProvider backoffClock,
        ILogger logger
    )
    {
        _run = run;
        _logger = logger;
        _waiting = new WaitingRows(defer, renew, policy, clock, backoffClock, logger);
    }

    /// <summary>Completes once <see cref="StopAsync"/> ran and every message that had started has finished.</summary>
    public Task Completion => _drained.Task;

    /// <summary>
    /// Hands <paramref name="work"/> to its consumer's sequencer, waiting while the sequencer is full.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the sequencer took the work and will run it or return it from
    /// <see cref="StopAsync"/>; <see langword="false"/> when the sequencers already stopped, so the caller still owns it.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled while waiting for room; the caller still owns the work.
    /// </exception>
    public async ValueTask<bool> EnqueueAsync(SequencedWork work, CancellationToken cancellationToken)
    {
        if (_stopped)
        {
            return false;
        }

        var sequencer = _sequencers.GetOrAdd(
            work.Descriptor.ResolvedConsumerIdentity,
            static (_, descriptor) => new Sequencer(descriptor.Concurrency),
            work.Descriptor
        );

        await sequencer.Room.WaitAsync(cancellationToken).ConfigureAwait(false);

        KeyQueue? start = null;
        lock (sequencer.Sync)
        {
            if (_stopped)
            {
                sequencer.Room.Release();
                return false;
            }

            var key = work.Message.RoutingAffinityKey;
            if (key is not null && sequencer.Keys.TryGetValue(key, out var active))
            {
                active.Pending.Enqueue(work);
                _waiting.Add(work);
                return true;
            }

            var queue = new KeyQueue(key);
            queue.Pending.Enqueue(work);
            if (key is not null)
            {
                sequencer.Keys.Add(key, queue);
            }

            if (sequencer.Running < sequencer.Concurrency)
            {
                sequencer.Running++;
                start = queue;
            }
            else
            {
                sequencer.Ready.Enqueue(queue);
                _waiting.Add(work);
            }
        }

        if (start is not null)
        {
            _StartRunner(sequencer, start);
        }

        return true;
    }

    /// <summary>
    /// Stops taking work and returns every message that had not started, so the caller can release what it holds for
    /// them. Messages already running finish on their own; <see cref="Completion"/> reports when they have.
    /// </summary>
    public async Task<IReadOnlyList<SequencedWork>> StopAsync()
    {
        _stopped = true;

        var unstarted = new List<SequencedWork>();
        foreach (var sequencer in _sequencers.Values)
        {
            lock (sequencer.Sync)
            {
                // A keyed queue is in Keys whether it runs or waits for a slot; only unkeyed queues live in Ready alone.
                foreach (var queue in sequencer.Keys.Values)
                {
                    _Drain(queue, sequencer, unstarted);
                }

                while (sequencer.Ready.TryDequeue(out var ready))
                {
                    _Drain(ready, sequencer, unstarted);
                }

                sequencer.Keys.Clear();
            }
        }

        // Settles the claim a pass in flight is renewing before the caller releases the claims of unstarted retries.
        await _waiting.StopAsync().ConfigureAwait(false);

        lock (_sync)
        {
            if (_running == 0)
            {
                _drained.TrySetResult();
            }
        }

        return unstarted;
    }

    private static void _Drain(KeyQueue queue, Sequencer sequencer, List<SequencedWork> unstarted)
    {
        while (queue.Pending.TryDequeue(out var work))
        {
            unstarted.Add(work);
            sequencer.Room.Release();
        }
    }

    private void _StartRunner(Sequencer sequencer, KeyQueue queue)
    {
        lock (_sync)
        {
            _running++;
        }

        _ = Task.Run(() => _RunAsync(sequencer, queue));
    }

    /// <summary>
    /// Holds one concurrency slot: runs the head of <paramref name="queue"/>, then the next key in line, until no key
    /// has work or the sequencers stop.
    /// </summary>
    private async Task _RunAsync(Sequencer sequencer, KeyQueue queue)
    {
        try
        {
            while (true)
            {
                SequencedWork? work;
                lock (sequencer.Sync)
                {
                    // Empty only when a stop drained the queue between two turns of this loop.
                    if (!queue.Pending.TryDequeue(out work))
                    {
                        sequencer.Running--;
                        return;
                    }
                }

                try
                {
                    await _waiting.TakeAsync(work).ConfigureAwait(false);
                    await _run(work).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // The slot outlives one message: a fault here must not strand the keys queued behind it.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    _logger.SequencedDispatchFailed(exception, work.Message.StorageId);
                }
                finally
                {
                    sequencer.Room.Release();
                }

                lock (sequencer.Sync)
                {
                    if (queue.Pending.Count == 0)
                    {
                        if (queue.Key is not null)
                        {
                            sequencer.Keys.Remove(queue.Key);
                        }

                        if (!sequencer.Ready.TryDequeue(out var next))
                        {
                            sequencer.Running--;
                            return;
                        }

                        queue = next;
                    }
                    else if (sequencer.Ready.TryDequeue(out var next))
                    {
                        // Other keys wait for a slot: this key takes its next turn behind them.
                        sequencer.Ready.Enqueue(queue);
                        queue = next;
                    }
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _running--;
                if (_stopped && _running == 0)
                {
                    _drained.TrySetResult();
                }
            }
        }
    }

    /// <summary>The queues and slots of one consumer identity.</summary>
    private sealed class Sequencer(byte concurrency)
    {
        public Lock Sync { get; } = new();

        public int Concurrency { get; } = Math.Max(1, (int)concurrency);

        /// <summary>The keys that have work running or queued.</summary>
        public Dictionary<string, KeyQueue> Keys { get; } = new(StringComparer.Ordinal);

        /// <summary>The queues waiting for a concurrency slot, oldest first.</summary>
        public Queue<KeyQueue> Ready { get; } = new();

        public int Running { get; set; }

        /// <summary>
        /// The room left in the buffer. Never disposed: a hand-over may still wait on it when the host stops, and it
        /// holds no handle unless its wait handle is read.
        /// </summary>
        public SemaphoreSlim Room { get; } = _CreateRoom(concurrency);

        private static SemaphoreSlim _CreateRoom(byte concurrency)
        {
            var capacity = Math.Max(_MinimumBuffer, Math.Max(1, (int)concurrency) * _BufferPerSlot);
            return new SemaphoreSlim(capacity, capacity);
        }
    }

    /// <summary>The messages of one key, in hand-over order; an unkeyed message gets a queue of its own.</summary>
    private sealed class KeyQueue(string? key)
    {
        public string? Key { get; } = key;

        public Queue<SequencedWork> Pending { get; } = new();
    }

    /// <summary>
    /// Keeps the rows that wait for their key or a slot out of retry pickup: it puts off the pickup of a first
    /// dispatch, and renews the claim a waiting retry already holds.
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
    private sealed class WaitingRows
#pragma warning restore CA1001
    {
        private const int _PassesPerDeferral = 2;

        private readonly HashSet<SequencedWork> _waiting = new(ReferenceEqualityComparer.Instance);
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
        public void Add(SequencedWork work)
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
        public async ValueTask TakeAsync(SequencedWork work)
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
                SequencedWork[] snapshot;
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
        private async ValueTask _HoldAsync(SequencedWork work)
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
                _logger.SequencedWaitHoldFailed(exception, message.StorageId);
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
}
