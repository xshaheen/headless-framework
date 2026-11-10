// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Tests.Streaming;

/// <summary>
/// A store several test hosts share, standing in for a module's database: each write stamps the next store sequence,
/// which is also the row's version, and a read returns the changes after a cursor, re-reading <see cref="Overlap"/>
/// sequences behind it as a real store's overlap window does.
/// </summary>
internal sealed class FakeChangeStore(TimeProvider time)
{
    private readonly Lock _lock = new();
    private readonly List<(long Marker, DashboardChange Change)> _log = [];
    private readonly Dictionary<string, Queue<DashboardChange[]>> _scripted = new(StringComparer.Ordinal);
    private readonly List<TaskCompletionSource> _starts = [];
    private readonly List<TaskCompletionSource> _stops = [];
    private readonly List<DateTimeOffset> _failures = [];
    private long _marker;
    private int _startCount;
    private int _readCount;
    private volatile bool _failing;

    /// <summary>Gets or sets how many sequences behind the cursor a read starts.</summary>
    public int Overlap { get; set; }

    /// <summary>Gets or sets a value indicating whether every read throws, as a store that is down does.</summary>
    public bool Failing
    {
        get => _failing;
        set => _failing = value;
    }

    /// <summary>Gets or sets the sources every read reports it could not reach, as a store spread over nodes does.</summary>
    public IReadOnlyList<string> UnavailableSources { get; set; } = [];

    public int StartCount => Volatile.Read(ref _startCount);

    public int ReadCount => Volatile.Read(ref _readCount);

    /// <summary>Gets the clock times of the reads that threw.</summary>
    public IReadOnlyList<DateTimeOffset> FailureTimes
    {
        get
        {
            lock (_lock)
            {
                return [.. _failures];
            }
        }
    }

    /// <summary>Changes one row: the change's version is the next store sequence.</summary>
    public DashboardChange Write(
        string module,
        string kind,
        string id,
        string? projectionJson = null,
        bool affectsAttention = false
    )
    {
        lock (_lock)
        {
            var marker = ++_marker;
            var change = Change(module, kind, id, marker.ToString(CultureInfo.InvariantCulture), projectionJson) with
            {
                AffectsAttention = affectsAttention,
            };
            _log.Add((marker, change));

            return change;
        }
    }

    /// <summary>Queues a batch that the module's next read returns as it is.</summary>
    public void Script(string module, params DashboardChange[] batch)
    {
        lock (_lock)
        {
            if (!_scripted.TryGetValue(module, out var queue))
            {
                queue = new Queue<DashboardChange[]>();
                _scripted[module] = queue;
            }

            queue.Enqueue(batch);
        }
    }

    /// <summary>Completes when the <paramref name="nth"/> reader asks for its start cursor.</summary>
    public Task Started(int nth) => _Signal(_starts, nth).Task;

    /// <summary>Completes when the <paramref name="nth"/> reader is stopped.</summary>
    public Task Stopped(int nth) => _Signal(_stops, nth).Task;

    public Task<string> StartCursorAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var nth = ++_startCount;
            _Signal(_starts, nth).TrySetResult();
            var stopped = _Signal(_stops, nth);
            _ = cancellationToken.Register(() => stopped.TrySetResult());

            if (_failing)
            {
                _failures.Add(time.GetUtcNow());
                throw new InvalidOperationException("The store is down.");
            }

            return Task.FromResult(_marker.ToString(CultureInfo.InvariantCulture));
        }
    }

    public Task<DashboardChangeBatch> ReadAsync(string module, string cursor)
    {
        lock (_lock)
        {
            _readCount++;

            if (_failing)
            {
                _failures.Add(time.GetUtcNow());
                throw new InvalidOperationException("The store is down.");
            }

            var next = _marker.ToString(CultureInfo.InvariantCulture);

            if (_scripted.TryGetValue(module, out var queue) && queue.TryDequeue(out var batch))
            {
                return Task.FromResult(
                    new DashboardChangeBatch
                    {
                        Changes = batch,
                        Cursor = next,
                        UnavailableSources = UnavailableSources,
                    }
                );
            }

            var from = long.Parse(cursor, CultureInfo.InvariantCulture) - Overlap;
            var changes = _log.Where(entry =>
                    entry.Marker > from && string.Equals(entry.Change.Target.Module, module, StringComparison.Ordinal)
                )
                .Select(static entry => entry.Change)
                .ToArray();

            return Task.FromResult(
                new DashboardChangeBatch
                {
                    Changes = changes,
                    Cursor = next,
                    UnavailableSources = UnavailableSources,
                }
            );
        }
    }

    /// <summary>Builds a change of one object, with an optional JSON projection.</summary>
    public static DashboardChange Change(
        string module,
        string kind,
        string id,
        string version,
        string? projectionJson = null
    )
    {
        JsonElement? projection = null;

        if (projectionJson is not null)
        {
            using var document = JsonDocument.Parse(projectionJson);
            projection = document.RootElement.Clone();
        }

        return new DashboardChange
        {
            Target = new ObjectRef
            {
                Module = module,
                Kind = kind,
                Id = id,
            },
            Version = version,
            Projection = projection,
        };
    }

    private TaskCompletionSource _Signal(List<TaskCompletionSource> signals, int nth)
    {
        lock (_lock)
        {
            while (signals.Count < nth)
            {
                signals.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            return signals[nth - 1];
        }
    }
}
