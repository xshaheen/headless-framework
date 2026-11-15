// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
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
/// <para>
/// A sequencer orders the work of one host. The run delegate orders a key across hosts: before a keyed message runs,
/// it asks the store whether an earlier message of the key is unfinished, a message waiting for a delayed retry
/// included, and puts a blocked message back instead of running it. For the sequencer such a message has finished.
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
    private TaskCompletionSource? _idle;
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
#pragma warning disable IDE0290 // Fix makes the code worse: a primary constructor would hold six parameters the class only passes on to its keeper.
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
        MessagingMetrics.RegisterSequencers(this);
    }
#pragma warning restore IDE0290

    /// <summary>Completes once <see cref="StopAsync"/> ran and every message that had started has finished.</summary>
    public Task Completion => _drained.Task;

    /// <summary>
    /// Completes once no slot is running, which also means nothing is queued: a slot ends only when no key has work.
    /// Retry pickup hands a sequenced row over without waiting for it, so a caller that needs the handed-over rows
    /// finished, such as a simulated run that moves the clock next, awaits this.
    /// </summary>
    public Task WhenIdleAsync()
    {
        lock (_sync)
        {
            if (_running == 0)
            {
                return Task.CompletedTask;
            }

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _idle.Task;
        }
    }

    /// <summary>
    /// The messages each consumer holds that have not started: queued behind an earlier message of their key, or
    /// waiting for a concurrency slot. A consumer appears once it has been handed work.
    /// </summary>
    public IEnumerable<(string ConsumerIdentity, int Waiting)> WaitingByConsumer()
    {
        foreach (var (consumerIdentity, sequencer) in _sequencers)
        {
            yield return (consumerIdentity, sequencer.Waiting);
        }
    }

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
                sequencer.AddWaiting();
                _waiting.Add(work.Waiting);
                return true;
            }

            var queue = new KeyQueue(key);
            queue.Pending.Enqueue(work);
            sequencer.AddWaiting();
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
                _waiting.Add(work.Waiting);
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
        MessagingMetrics.UnregisterSequencers(this);

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
            sequencer.RemoveWaiting();
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

                    sequencer.RemoveWaiting();
                }

                try
                {
                    await _waiting.TakeAsync(work.Waiting).ConfigureAwait(false);
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
                if (_running == 0)
                {
                    _idle?.TrySetResult();
                    _idle = null;
                }

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

        private int _waiting;

        /// <summary>
        /// The messages queued and not yet started. Changed under <see cref="Sync"/>; a metric collection reads it
        /// without taking the lock.
        /// </summary>
        public int Waiting => Volatile.Read(ref _waiting);

        /// <summary>Counts a message queued; call under <see cref="Sync"/>.</summary>
        public void AddWaiting() => _waiting++;

        /// <summary>Counts a queued message that started or was returned; call under <see cref="Sync"/>.</summary>
        public void RemoveWaiting() => _waiting--;

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
}
