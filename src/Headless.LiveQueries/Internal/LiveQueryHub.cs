// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Holds the live subscriptions of this process and signals the ones whose recorded reads overlap a committed
/// write. It never runs a query itself: a signalled subscription re-runs on the flow of the caller that enumerates
/// it, which is the flow that carries that caller's ambient state, such as its tenant.
/// </summary>
/// <remarks>
/// A write announced on the flow of a tagged command is also acknowledged: the command's id goes to the
/// subscriptions its sender holds, and to no other, once their result is known to reflect the write.
/// </remarks>
internal sealed partial class LiveQueryHub(
    IOptions<LiveQueriesOptions> options,
    LiveQueriesMetrics metrics,
    ILogger<LiveQueryHub>? logger = null
)
{
    private const int _SaltLength = 32;

    // Half a SHA-256 MAC: far more than change detection or token uniqueness within one subscription needs.
    private const int _VersionTokenBytes = 16;

    // What one subscription holds for a client that stopped reading its stream and keeps sending commands. Past it
    // the oldest id goes, so such a client costs the host a bounded amount of memory.
    private const int _MaxPendingAcknowledgements = 1024;

    private readonly int _maxTrackedKeys = options.Value.MaxTrackedKeys;
    private readonly ILogger _logger = logger ?? NullLogger<LiveQueryHub>.Instance;
    private readonly ConcurrentDictionary<Subscription, byte> _subscriptions = new();

    // The version of the last write to each key. Written under _writeLock, read without it.
    private readonly ConcurrentDictionary<string, long> _keyVersions = new(StringComparer.Ordinal);

    // Every (key, version) written, oldest first, so trimming finds the keys changed longest ago without a scan.
    // An entry is out of date once its key was written again; trimming drops those as it meets them.
    private readonly Queue<(string Key, long Version)> _writeOrder = new();
    private readonly Lock _writeLock = new();
    private int _trackedKeys;
    private long _version;

    // The highest version of any key trimmed from _keyVersions. A key with no entry may have changed at any version
    // up to this one.
    private long _evictedFloor;

    /// <summary>Records what a commit changed and signals the subscriptions it could affect.</summary>
    /// <param name="writes">What the commit changed. The commit must already be durable.</param>
    public void Invalidate(LiveWriteSet writes)
    {
        if (writes.IsEmpty)
        {
            return;
        }

        // Read on the announcing flow, which is the command's own when the commit happened on its request.
        var command = LiveCommandContext.Current is { } current && current.TryAnnounce() ? current : null;
        long version;

        lock (_writeLock)
        {
            // Published before the keys are stamped: a run that reads this version started after the commit, so its
            // result already holds the write, and a run that read the previous one finds the stamped keys newer.
            version = _version + 1;
            Volatile.Write(ref _version, version);

            foreach (var key in writes.Keys)
            {
                _Stamp(key, version);
            }

            foreach (var row in writes.Rows)
            {
                _Stamp(row.Key, version);
            }

            _Trim();
        }

        var subscriptions = 0;
        var woken = 0;

        foreach (var (subscription, _) in _subscriptions)
        {
            subscriptions++;

            var overlaps = subscription.Dependencies.Overlaps(writes, out var ruledOutRows);

            if (overlaps)
            {
                woken++;
                metrics.SubscriptionWoken();
            }
            else if (ruledOutRows > 0)
            {
                metrics.RowsRuledOut(ruledOutRows);
            }

            // Session and user both: a session id that leaked or was guessed tells another user nothing.
            var acknowledged = command?.Sender.Equals(subscription.Subscriber) is true ? command.CommandId : null;

            if (overlaps || acknowledged is not null)
            {
                subscription.Notify(overlaps, acknowledged, version);
            }
        }

        _LogInvalidated(_logger, writes.Keys.Count, writes.Rows.Count, woken, subscriptions);
    }

    /// <summary>
    /// Returns whether a subscription read <paramref name="key" /> through row filters in its last run, so the rows
    /// of a write to the key could spare it a re-run. A writer that hears <see langword="false" /> announces the
    /// key whole and need not describe its rows.
    /// </summary>
    /// <remarks>
    /// A subscription whose first run has not finished, or that starts after this call, is not counted. It is still
    /// served: a row is matched against whatever a subscription has read by the time the write is announced, and a
    /// run that races the announcement is checked against the key, which every row stamps.
    /// </remarks>
    public bool HasNarrowedReader(string key)
    {
        foreach (var subscription in _subscriptions)
        {
            if (subscription.Key.Dependencies.IsNarrowed(key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs <paramref name="run" /> now and again after each write that touches what it read, and yields an update
    /// for the first result, for each result that differs from the last one yielded, and for a failure, which ends
    /// the sequence.
    /// </summary>
    /// <param name="run">
    /// One run of the query. It begins a <see cref="LiveQueryRecorder" /> in the frame that awaits the query and
    /// returns that recorder's snapshot with the result bytes.
    /// </param>
    /// <param name="cancellationToken">Ends the subscription.</param>
    /// <exception cref="LiveQueryHasNoDependenciesException">A successful run recorded no dependency.</exception>
    public IAsyncEnumerable<LiveUpdate> WatchAsync(
        Func<CancellationToken, ValueTask<LiveRunResult>> run,
        CancellationToken cancellationToken
    )
    {
        return WatchAsync(run, enterRerun: null, subscriber: null, cancellationToken);
    }

    /// <summary>
    /// Watches as <see cref="WatchAsync(Func{CancellationToken, ValueTask{LiveRunResult}}, CancellationToken)" />
    /// does, and lets the caller hold back every run after the first.
    /// </summary>
    /// <param name="run">One run of the query; see the other overload.</param>
    /// <param name="enterRerun">
    /// Awaited on the enumerating caller's flow before each run after the first. It returns when the run may start,
    /// with a turn that is disposed when the run ends, or <see langword="null" /> for nothing to release. A caller
    /// uses it to space the runs of one subscription and to bound the runs in flight across subscriptions.
    /// </param>
    /// <param name="subscriber">
    /// Who holds the subscription, or <see langword="null" /> for a subscription that is told of no command. The
    /// commands this subscriber sends are acknowledged in <see cref="LiveUpdate.AcknowledgedCommands" />: with the
    /// first result known to reflect the command's write, or alone, as <see cref="LiveUpdateKind.Acknowledged" />,
    /// when that result equals the last one yielded or the write touched nothing the query read.
    /// </param>
    /// <param name="cancellationToken">Ends the subscription.</param>
    /// <exception cref="LiveQueryHasNoDependenciesException">A successful run recorded no dependency.</exception>
    public async IAsyncEnumerable<LiveUpdate> WatchAsync(
        Func<CancellationToken, ValueTask<LiveRunResult>> run,
        Func<CancellationToken, ValueTask<IDisposable?>>? enterRerun,
        LiveSubscriber? subscriber,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var subscription = new Subscription(subscriber);
        _subscriptions.TryAdd(subscription, 0);
        metrics.SubscriptionOpened();

        try
        {
            // A key of this subscription alone, so its tokens say nothing a subscriber could match against another
            // subscription's tokens, and nothing about how many writes the process has seen.
            var salt = RandomNumberGenerator.GetBytes(_SaltLength);
            byte[]? lastMac = null;
            var firstRun = true;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Entered before the version is read: a write that lands while the subscription waits for its turn
                // is then read by the run itself, instead of making the run stale and causing one more.
                var turn =
                    firstRun || enterRerun is null ? null : await enterRerun(cancellationToken).ConfigureAwait(false);

                firstRun = false;
                long startVersion;
                LiveRunResult result;

                try
                {
                    // Read before the run: a write committed after this point either lands in the run's result or
                    // stamps a key above this version, which the staleness check below catches.
                    startVersion = Volatile.Read(ref _version);

                    // A request to run again that is already waiting was raised by a write this run will see or the
                    // staleness check will catch, so acting on it again after the run would only repeat the run.
                    subscription.BeginRun();
                    metrics.RunStarted();
                    result = await _RunAsync(run, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    turn?.Dispose();
                }

                subscription.Dependencies = result.Dependencies;

                var mac = HMACSHA256.HashData(salt, result.Payload.Span);
                var token = Convert.ToHexStringLower(mac.AsSpan(0, _VersionTokenBytes));

                if (result.Failed)
                {
                    metrics.RunFailed();

                    yield return new LiveUpdate
                    {
                        Kind = LiveUpdateKind.Failed,
                        Payload = result.Payload,
                        Version = token,
                    };

                    yield break;
                }

                if (result.Dependencies.IsEmpty)
                {
                    throw new LiveQueryHasNoDependenciesException();
                }

                // Taken before the staleness check, so the check below speaks for the write of every command taken:
                // a command is held only after its write's keys were stamped.
                var held = subscription.TakeAcknowledgements();

                // Checked per key, not per row: a write that raced this run re-runs it even when a row filter would
                // have ruled it out, which costs one run and can never miss a change.
                var stale = _IsStale(result.Dependencies, startVersion);
                var acknowledged = _Acknowledge(subscription, held, stale, startVersion);

                if (lastMac is null || !mac.AsSpan().SequenceEqual(lastMac))
                {
                    yield return new LiveUpdate
                    {
                        Kind = lastMac is null ? LiveUpdateKind.Initial : LiveUpdateKind.Changed,
                        Payload = result.Payload,
                        Version = token,
                        AcknowledgedCommands = acknowledged,
                    };
                }
                else if (acknowledged.Length > 0)
                {
                    yield return _Acknowledgement(token, acknowledged);
                }

                lastMac = mac;

                if (stale)
                {
                    continue;
                }

                // Woken to run again, or only to pass on commands whose writes touched nothing this query read.
                while (await subscription.WaitAsync(cancellationToken).ConfigureAwait(false) is { } untouched)
                {
                    if (untouched.Length > 0)
                    {
                        yield return _Acknowledgement(token, untouched);
                    }
                }
            }
        }
        finally
        {
            _subscriptions.TryRemove(subscription, out _);
            metrics.SubscriptionClosed();
        }
    }

    // A command whose write was published before the run started is in the run's result. When the run is not stale,
    // no later write touched a key it read, so every other command held left the result as it is and is acknowledged
    // with it. When the run is stale, those go back and wait for the run that follows.
    private static string[] _Acknowledge(
        Subscription subscription,
        List<(string Command, long Version)>? held,
        bool stale,
        long startVersion
    )
    {
        if (held is null)
        {
            return [];
        }

        List<string> commands = new(held.Count);
        List<(string Command, long Version)>? early = null;

        foreach (var command in held)
        {
            if (stale && command.Version > startVersion)
            {
                (early ??= []).Add(command);
            }
            else
            {
                commands.Add(command.Command);
            }
        }

        if (early is not null)
        {
            subscription.HoldAgain(early);
        }

        return [.. commands];
    }

    private static LiveUpdate _Acknowledgement(string lastToken, string[] commands)
    {
        return new LiveUpdate
        {
            Kind = LiveUpdateKind.Acknowledged,
            Payload = ReadOnlyMemory<byte>.Empty,
            Version = lastToken,
            AcknowledgedCommands = commands,
        };
    }

    private async ValueTask<LiveRunResult> _RunAsync(
        Func<CancellationToken, ValueTask<LiveRunResult>> run,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await run(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            metrics.RunFailed();

            throw;
        }
    }

    private void _Stamp(string key, long version)
    {
        if (_keyVersions.TryGetValue(key, out var current))
        {
            if (current == version)
            {
                // The same key twice in one write set, as every row of one table is.
                return;
            }

            _keyVersions[key] = version;
        }
        else
        {
            _keyVersions[key] = version;
            _trackedKeys++;
        }

        _writeOrder.Enqueue((key, version));
    }

    private void _Trim()
    {
        // The second bound keeps the queue from growing without limit when a few keys are written again and again.
        while (_trackedKeys > _maxTrackedKeys || _writeOrder.Count > 2L * _maxTrackedKeys)
        {
            var (key, version) = _writeOrder.Dequeue();

            if (!_keyVersions.TryGetValue(key, out var current) || current != version)
            {
                continue;
            }

            // Raised before the entry goes: a reader that misses the entry must already see the floor that
            // replaces it.
            Volatile.Write(ref _evictedFloor, version);
            _keyVersions.TryRemove(key, out _);
            _trackedKeys--;
        }
    }

    private bool _IsStale(LiveDependencies dependencies, long startVersion)
    {
        foreach (var key in dependencies.Keys)
        {
            if (_keyVersions.TryGetValue(key, out var version))
            {
                if (version > startVersion)
                {
                    return true;
                }
            }
            else if (Volatile.Read(ref _evictedFloor) > startVersion)
            {
                // The key has no entry, so it never changed or its entry was trimmed. A trimmed entry may be the
                // write this run raced.
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "LiveQueriesInvalidated",
        Level = LogLevel.Debug,
        Message = "Live-query invalidation of {KeyCount} keys and {RowCount} rows signalled {WokenCount} of {SubscriptionCount} subscriptions"
    )]
    private static partial void _LogInvalidated(
        ILogger logger,
        int keyCount,
        int rowCount,
        int wokenCount,
        int subscriptionCount
    );

    private sealed class Subscription(LiveSubscriber? subscriber)
    {
        // Capacity one: any number of notifications while the subscription is busy collapse into a single wake.
        private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
        );

        // Guards the two fields below together with the wake, so a command's id and the request to run again that
        // its write raised are always seen as one.
        private readonly Lock _lock = new();
        private List<(string Command, long Version)>? _acknowledgements;
        private bool _rerun;

        private volatile LiveDependencies _dependencies = LiveDependencies.None;

        public LiveSubscriber? Subscriber => subscriber;

        // What the last finished run read. Empty until the first run finishes; the staleness check covers that run.
        public LiveDependencies Dependencies
        {
            get => _dependencies;
            set => _dependencies = value;
        }

        /// <summary>Wakes the subscription for a write committed as <paramref name="version" />.</summary>
        /// <param name="rerun">Whether the write could change the result, so the query must run again.</param>
        /// <param name="command">The id to acknowledge to this subscription, if the write was its client's command.</param>
        /// <param name="version">The version the write was published as.</param>
        public void Notify(bool rerun, string? command, long version)
        {
            lock (_lock)
            {
                _rerun |= rerun;

                if (command is not null)
                {
                    _Hold(command, version);
                }

                _wake.Writer.TryWrite(0);
            }
        }

        /// <summary>Forgets a request to run again: the run that is about to start answers it.</summary>
        public void BeginRun()
        {
            lock (_lock)
            {
                _rerun = false;
                _wake.Reader.TryRead(out _);
            }
        }

        /// <summary>
        /// Waits to be woken. Returns <see langword="null" /> when the query must run again; otherwise the commands
        /// to acknowledge without a run, because their writes touched nothing the query read.
        /// </summary>
        public async ValueTask<string[]?> WaitAsync(CancellationToken cancellationToken)
        {
            await _wake.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                // Decided and taken in one step: a command whose write does call for a run is then never passed
                // on ahead of that run.
                if (_rerun)
                {
                    return null;
                }

                if (_acknowledgements is not { Count: > 0 } held)
                {
                    return [];
                }

                var commands = new string[held.Count];

                for (var index = 0; index < commands.Length; index++)
                {
                    commands[index] = held[index].Command;
                }

                held.Clear();

                return commands;
            }
        }

        /// <summary>Removes and returns every command held, oldest first, or <see langword="null" /> when none is.</summary>
        public List<(string Command, long Version)>? TakeAcknowledgements()
        {
            lock (_lock)
            {
                if (_acknowledgements is not { Count: > 0 } held)
                {
                    return null;
                }

                _acknowledgements = null;

                return held;
            }
        }

        /// <summary>Puts back commands that were taken too early, ahead of the ones held since.</summary>
        public void HoldAgain(List<(string Command, long Version)> early)
        {
            lock (_lock)
            {
                if (_acknowledgements is { Count: > 0 } later)
                {
                    foreach (var command in later)
                    {
                        // Held again since with a later write: that entry stands for both.
                        early.RemoveAll(taken =>
                            string.Equals(taken.Command, command.Command, StringComparison.Ordinal)
                        );
                        early.Add(command);
                    }
                }

                _acknowledgements = early;
            }
        }

        private void _Hold(string command, long version)
        {
            var held = _acknowledgements ??= [];

            // A command that commits more than once is acknowledged once, with the result that holds its last
            // write so far.
            for (var index = 0; index < held.Count; index++)
            {
                if (string.Equals(held[index].Command, command, StringComparison.Ordinal))
                {
                    held.RemoveAt(index);

                    break;
                }
            }

            if (held.Count == _MaxPendingAcknowledgements)
            {
                held.RemoveAt(0);
            }

            held.Add((command, version));
        }
    }
}
