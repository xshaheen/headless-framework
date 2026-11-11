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
internal sealed partial class LiveQueryHub(
    IOptions<LiveQueriesOptions> options,
    LiveQueriesMetrics metrics,
    ILogger<LiveQueryHub>? logger = null
)
{
    private const int _SaltLength = 32;

    // Half a SHA-256 MAC: far more than change detection or token uniqueness within one subscription needs.
    private const int _VersionTokenBytes = 16;

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

        foreach (var subscription in _subscriptions.Keys)
        {
            subscriptions++;

            if (subscription.Dependencies.Overlaps(writes, out var ruledOutRows))
            {
                woken++;
                metrics.SubscriptionWoken();
                subscription.Signal();
            }
            else if (ruledOutRows > 0)
            {
                metrics.RowsRuledOut(ruledOutRows);
            }
        }

        _LogInvalidated(_logger, writes.Keys.Count, writes.Rows.Count, woken, subscriptions);
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
    public async IAsyncEnumerable<LiveUpdate> WatchAsync(
        Func<CancellationToken, ValueTask<LiveRunResult>> run,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var subscription = new Subscription();
        _subscriptions.TryAdd(subscription, 0);
        metrics.SubscriptionOpened();

        try
        {
            // A key of this subscription alone, so its tokens say nothing a subscriber could match against another
            // subscription's tokens, and nothing about how many writes the process has seen.
            var salt = RandomNumberGenerator.GetBytes(_SaltLength);
            byte[]? lastMac = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Read before the run: a write committed after this point either lands in the run's result or stamps
                // a key above this version, which the staleness check below catches.
                var startVersion = Volatile.Read(ref _version);

                // A signal already waiting was raised by a write this run will see or the staleness check will
                // catch, so acting on it again after the run would only repeat the run.
                subscription.ClearSignal();
                metrics.RunStarted();
                var result = await _RunAsync(run, cancellationToken).ConfigureAwait(false);
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

                if (lastMac is null || !mac.AsSpan().SequenceEqual(lastMac))
                {
                    yield return new LiveUpdate
                    {
                        Kind = lastMac is null ? LiveUpdateKind.Initial : LiveUpdateKind.Changed,
                        Payload = result.Payload,
                        Version = token,
                    };
                }

                lastMac = mac;

                // Checked per key, not per row: a write that raced this run re-runs it even when a row filter would
                // have ruled it out, which costs one run and can never miss a change.
                if (_IsStale(result.Dependencies, startVersion))
                {
                    continue;
                }

                await subscription.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _subscriptions.TryRemove(subscription, out _);
            metrics.SubscriptionClosed();
        }
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

    private sealed class Subscription
    {
        // Capacity one: any number of invalidations while the subscription is busy collapse into a single re-run.
        private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
        );

        private volatile LiveDependencies _dependencies = LiveDependencies.None;

        // What the last finished run read. Empty until the first run finishes; the staleness check covers that run.
        public LiveDependencies Dependencies
        {
            get => _dependencies;
            set => _dependencies = value;
        }

        public void Signal() => _signal.Writer.TryWrite(0);

        public void ClearSignal() => _signal.Reader.TryRead(out _);

        public async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            await _signal.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
