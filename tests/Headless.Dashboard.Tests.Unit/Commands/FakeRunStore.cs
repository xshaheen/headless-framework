// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Tests.Commands;

/// <summary>
/// An in-memory run store shared by every node of a test, with its own clock: a counter that stamps the time a row
/// enters a state, as a real store's clock would. Requeue is a conditional update, so a row leaves the failed state
/// once and is never requeued twice.
/// </summary>
internal sealed class FakeRunStore
{
    private readonly Lock _lock = new();
    private readonly SortedDictionary<string, FakeRun> _runs = new(StringComparer.Ordinal);
    private long _clock;

    public static string RunId(int number) => $"run-{number:D6}";

    /// <summary>Adds failed runs numbered <paramref name="first"/> onward.</summary>
    public void SeedFailed(int count, int first = 1, string queue = "default")
    {
        lock (_lock)
        {
            for (var i = first; i < first + count; i++)
            {
                var id = RunId(i);
                _runs[id] = new FakeRun(id, queue, FakeRunState.Failed, ++_clock, Requeues: 0);
            }
        }
    }

    /// <summary>Moves a run to <paramref name="state"/>, stamping the time it entered it.</summary>
    public void SetState(string id, FakeRunState state)
    {
        lock (_lock)
        {
            _runs[id] = _runs[id] with { State = state, EnteredAt = ++_clock };
        }
    }

    public FakeRun Get(string id)
    {
        lock (_lock)
        {
            return _runs[id];
        }
    }

    public IReadOnlyList<FakeRun> All()
    {
        lock (_lock)
        {
            return [.. _runs.Values];
        }
    }

    public DashboardOperationScope Prepare(string? queue, int countLimit, int sampleSize)
    {
        lock (_lock)
        {
            var inScope = _runs.Values.Where(run => run.State == FakeRunState.Failed && _Matches(run, queue));
            long count = 0;
            long mark = 0;
            var sample = new List<DashboardObject>();

            foreach (var run in inScope)
            {
                if (count > countLimit)
                {
                    break;
                }

                count++;
                mark = Math.Max(mark, run.EnteredAt);

                if (sample.Count < sampleSize)
                {
                    sample.Add(FakeCommandModule.ToObject(run));
                }
            }

            return new DashboardOperationScope
            {
                Count = count,
                Sample = sample,
                HighWaterMark = mark.ToString(CultureInfo.InvariantCulture),
            };
        }
    }

    public DashboardOperationChunk Chunk(string? queue, long mark, string? cursor, int size)
    {
        lock (_lock)
        {
            var walked = _runs
                .Values.Where(run =>
                    (cursor is null || string.CompareOrdinal(run.Id, cursor) > 0) && _Matches(run, queue)
                )
                .Take(size)
                .ToArray();

            var rows = walked.Select(run => _RequeueIfInScope(run.Id, mark)).ToArray();

            return new DashboardOperationChunk
            {
                Rows = rows,
                NextCursor = walked.Length < size ? null : walked[^1].Id,
            };
        }
    }

    public DashboardRowResult Requeue(string id)
    {
        lock (_lock)
        {
            return _RequeueIfInScope(id, long.MaxValue);
        }
    }

    // The conditional update: only a run still failed, and failed at or before the mark, is requeued.
    private DashboardRowResult _RequeueIfInScope(string id, long mark)
    {
        var run = _runs[id];

        if (run.State != FakeRunState.Failed)
        {
            var reason = run.State == FakeRunState.Running ? "Running - cancel instead" : "no longer failed";
            return new DashboardRowResult
            {
                Id = id,
                Outcome = DashboardRowOutcome.Skipped,
                Reason = reason,
            };
        }

        if (run.EnteredAt > mark)
        {
            return new DashboardRowResult
            {
                Id = id,
                Outcome = DashboardRowOutcome.Skipped,
                Reason = "failed after the Operation was confirmed",
            };
        }

        _runs[id] = run with { State = FakeRunState.Queued, EnteredAt = ++_clock, Requeues = run.Requeues + 1 };

        return new DashboardRowResult { Id = id, Outcome = DashboardRowOutcome.Done };
    }

    private static bool _Matches(FakeRun run, string? queue)
    {
        return queue is null || string.Equals(run.Queue, queue, StringComparison.Ordinal);
    }
}

/// <summary>The state of one fake run.</summary>
internal enum FakeRunState
{
    Failed = 0,
    Running = 1,
    Queued = 2,
}

/// <summary>One fake run: its queue, state, the store-clock time it entered that state, and how often it was requeued.</summary>
internal sealed record FakeRun(string Id, string Queue, FakeRunState State, long EnteredAt, int Requeues);
