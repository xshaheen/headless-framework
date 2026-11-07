// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;

namespace Headless.Dashboard;

/// <summary>
/// The diagnostic module's in-process store of fake runs, which the dashboard can fail, requeue, annotate, and relate.
/// It stands in for a feature module's real store, so it keeps the guarantees the module contract asks of one: every
/// change is stamped from a store sequence, and a requeue is a conditional update.
/// </summary>
/// <remarks>
/// <para>
/// The store keeps nothing outside the process: no table, cache key, or channel. Every write takes one lock and stamps
/// the run with the next value of the store's sequence, so the sequence is the store's clock: it orders every change,
/// serves as the change cursor, and marks the time a run entered its current status. Display times come from the
/// <see cref="TimeProvider"/> given to the constructor.
/// </para>
/// <para>
/// One instance can be shared by several hosts, through
/// <see cref="DiagnosticsOptions.UseStore(DiagnosticsStore)"/>, so a test runs two dashboard nodes over one store as a
/// real deployment shares one database. The store is thread-safe.
/// </para>
/// </remarks>
/// <param name="timeProvider">The clock the run times are read from.</param>
/// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
[PublicAPI]
public sealed class DiagnosticsStore(TimeProvider timeProvider)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DiagnosticsRun> _runs = new(StringComparer.Ordinal);
    private readonly List<(string RunId, ObjectRef Target)> _links = [];
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private long _sequence;
    private long _nextRunNumber;

    /// <summary>Creates an empty store whose display times come from the system clock.</summary>
    public DiagnosticsStore()
        : this(TimeProvider.System) { }

    /// <summary>Gets the store's current sequence: the stamp of its latest change, or 0 before the first.</summary>
    public long Sequence
    {
        get
        {
            lock (_lock)
            {
                return _sequence;
            }
        }
    }

    /// <summary>Gets every run, newest first.</summary>
    public IReadOnlyList<DiagnosticsRun> Runs
    {
        get
        {
            lock (_lock)
            {
                return [.. _runs.Values.OrderByDescending(static run => run.CreatedSequence)];
            }
        }
    }

    /// <summary>Adds a queued run with a new W3C trace id.</summary>
    /// <returns>The new run.</returns>
    public DiagnosticsRun AddRun()
    {
        return AddRun(ActivityTraceId.CreateRandom().ToHexString());
    }

    /// <summary>Adds a queued run that carries <paramref name="traceId"/>.</summary>
    /// <param name="traceId">The W3C trace id the run carries.</param>
    /// <returns>The new run.</returns>
    /// <exception cref="ArgumentException"><paramref name="traceId"/> is <see langword="null"/>, empty, or white space.</exception>
    public DiagnosticsRun AddRun(string traceId)
    {
        Argument.IsNotNullOrWhiteSpace(traceId);

        lock (_lock)
        {
            var number = ++_nextRunNumber;
            var id = "run-" + number.ToString("D6", CultureInfo.InvariantCulture);
            var sequence = ++_sequence;
            var now = _timeProvider.GetUtcNow();

            var run = new DiagnosticsRun
            {
                Id = id,
                Status = DiagnosticsRunStatus.Queued,
                Attempts = 0,
                LastError = null,
                TraceId = traceId,
                CreatedAt = now,
                UpdatedAt = now,
                StatusChangedAt = now,
                CreatedSequence = sequence,
                Version = sequence,
                StatusSequence = sequence,
                Payload = _Payload(number),
                Timeline = [new DiagnosticsRunEvent { Kind = DiagnosticsRunEventKind.Created, At = now }],
            };

            _runs[id] = run;

            return run;
        }
    }

    /// <summary>Returns the run with id <paramref name="id"/>, or <see langword="null"/> when there is none.</summary>
    /// <param name="id">The run's id.</param>
    /// <returns>The run, or <see langword="null"/>.</returns>
    public DiagnosticsRun? Find(string id)
    {
        lock (_lock)
        {
            return _runs.GetValueOrDefault(id);
        }
    }

    /// <summary>Starts a run: moves it to <see cref="DiagnosticsRunStatus.Running"/> and counts an attempt.</summary>
    /// <param name="id">The run's id.</param>
    /// <returns>The changed run.</returns>
    /// <exception cref="KeyNotFoundException">No run has the id <paramref name="id"/>.</exception>
    /// <exception cref="InvalidOperationException">The run is already running.</exception>
    public DiagnosticsRun Start(string id)
    {
        return _Expect(TryStart(id), id);
    }

    /// <summary>Completes a run: moves it to <see cref="DiagnosticsRunStatus.Succeeded"/>.</summary>
    /// <param name="id">The run's id.</param>
    /// <returns>The changed run.</returns>
    /// <exception cref="KeyNotFoundException">No run has the id <paramref name="id"/>.</exception>
    /// <exception cref="InvalidOperationException">The run already succeeded.</exception>
    public DiagnosticsRun Succeed(string id)
    {
        return _Expect(_Transition(id, DiagnosticsRunStatus.Succeeded, error: null), id);
    }

    /// <summary>Fails a run with <paramref name="error"/>.</summary>
    /// <param name="id">The run's id.</param>
    /// <param name="error">The error message, which Needs you groups failed runs by.</param>
    /// <returns>The changed run.</returns>
    /// <exception cref="ArgumentException"><paramref name="error"/> is <see langword="null"/>, empty, or white space.</exception>
    /// <exception cref="KeyNotFoundException">No run has the id <paramref name="id"/>.</exception>
    /// <exception cref="InvalidOperationException">The run already failed.</exception>
    public DiagnosticsRun Fail(string id, string error)
    {
        Argument.IsNotNullOrWhiteSpace(error);

        return _Expect(TryFail(id, error), id);
    }

    /// <summary>Requeues a failed run: moves it back to <see cref="DiagnosticsRunStatus.Queued"/>.</summary>
    /// <param name="id">The run's id.</param>
    /// <returns>The changed run.</returns>
    /// <exception cref="KeyNotFoundException">No run has the id <paramref name="id"/>.</exception>
    /// <exception cref="InvalidOperationException">The run is not failed.</exception>
    public DiagnosticsRun Requeue(string id)
    {
        return _Expect(TryRequeue(id, long.MaxValue), id);
    }

    /// <summary>Adds a note to a run's timeline.</summary>
    /// <param name="id">The run's id.</param>
    /// <param name="text">The note.</param>
    /// <returns>The changed run.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is <see langword="null"/>, empty, or white space.</exception>
    /// <exception cref="KeyNotFoundException">No run has the id <paramref name="id"/>.</exception>
    public DiagnosticsRun AddNote(string id, string text)
    {
        Argument.IsNotNullOrWhiteSpace(text);

        return _Expect(TryAddNote(id, text), id);
    }

    /// <summary>
    /// Relates a run to <paramref name="target"/>, an object of any module, so the dashboard lists the run among that
    /// object's related objects.
    /// </summary>
    /// <param name="id">The run's id.</param>
    /// <param name="target">The object to relate the run to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No run has the id <paramref name="id"/>.</exception>
    public void Link(string id, ObjectRef target)
    {
        Argument.IsNotNull(target);

        lock (_lock)
        {
            if (!_runs.ContainsKey(id))
            {
                throw new KeyNotFoundException($"No diagnostic run has the id '{id}'.");
            }

            if (!_links.Contains((id, target)))
            {
                _links.Add((id, target));
            }
        }
    }

    internal StoreWrite TryStart(string id)
    {
        return _Transition(id, DiagnosticsRunStatus.Running, error: null);
    }

    internal StoreWrite TryFail(string id, string error)
    {
        return _Transition(id, DiagnosticsRunStatus.Failed, error);
    }

    /// <summary>
    /// The conditional update behind Requeue: only a run that is still failed, and entered that status at or before
    /// <paramref name="highWaterMark"/>, is requeued.
    /// </summary>
    internal StoreWrite TryRequeue(string id, long highWaterMark)
    {
        lock (_lock)
        {
            return _RequeueLocked(id, highWaterMark);
        }
    }

    internal StoreWrite TryAddNote(string id, string text)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(id, out var run))
            {
                return StoreWrite.NotFound(id);
            }

            var now = _timeProvider.GetUtcNow();
            var changed = run with
            {
                Version = ++_sequence,
                UpdatedAt = now,
                Timeline =
                [
                    .. run.Timeline,
                    new DiagnosticsRunEvent
                    {
                        Kind = DiagnosticsRunEventKind.Note,
                        At = now,
                        Text = text,
                    },
                ],
            };
            _runs[id] = changed;

            return StoreWrite.Done(changed, affectsAttention: false);
        }
    }

    /// <summary>Returns the runs changed after <paramref name="after"/>, oldest change first, at most <paramref name="max"/>.</summary>
    /// <remarks>
    /// Every write stamps its run under the store's lock, so changes become visible in stamp order and a read needs no
    /// overlap window. A run changed twice since the cursor is read once, at its latest version.
    /// </remarks>
    internal IReadOnlyList<DiagnosticsRun> ReadChanges(long after, int max)
    {
        lock (_lock)
        {
            return [.. _runs.Values.Where(run => run.Version > after).OrderBy(static run => run.Version).Take(max)];
        }
    }

    /// <summary>Returns the runs linked to <paramref name="target"/>, newest first.</summary>
    internal IReadOnlyList<DiagnosticsRun> LinkedTo(ObjectRef target)
    {
        lock (_lock)
        {
            return
            [
                .. _links
                    .Where(link => link.Target == target)
                    .Select(link => _runs[link.RunId])
                    .Distinct()
                    .OrderByDescending(static run => run.CreatedSequence),
            ];
        }
    }

    /// <summary>
    /// Freezes the scope of a requeue over a filter: counts the failed runs that match, up to one past
    /// <paramref name="countLimit"/>, and reads the high-water mark, the latest sequence at which one of them failed.
    /// </summary>
    internal (long Count, IReadOnlyList<DiagnosticsRun> Sample, long HighWaterMark) PrepareRequeue(
        DiagnosticsRunFilter filter,
        int countLimit,
        int sampleSize
    )
    {
        lock (_lock)
        {
            long count = 0;
            long mark = 0;
            var sample = new List<DiagnosticsRun>(sampleSize);

            foreach (var run in _runs.Values.Where(r => r.Status == DiagnosticsRunStatus.Failed && filter.Matches(r)))
            {
                if (count > countLimit)
                {
                    break;
                }

                count++;
                mark = Math.Max(mark, run.StatusSequence);

                if (sample.Count < sampleSize)
                {
                    sample.Add(run);
                }
            }

            return (count, sample, mark);
        }
    }

    /// <summary>
    /// Requeues the next chunk of a frozen scope: the failed runs that match, failed at or before
    /// <paramref name="highWaterMark"/>, and failed after <paramref name="cursor"/>, in the order they failed.
    /// </summary>
    /// <returns>The changed runs and the cursor to resume from, or <see langword="null"/> when the scope is done.</returns>
    internal (IReadOnlyList<StoreWrite> Writes, long? NextCursor) RequeueChunk(
        DiagnosticsRunFilter filter,
        long highWaterMark,
        long cursor,
        int chunkSize
    )
    {
        lock (_lock)
        {
            var walked = _runs
                .Values.Where(run =>
                    run.Status == DiagnosticsRunStatus.Failed
                    && run.StatusSequence > cursor
                    && run.StatusSequence <= highWaterMark
                    && filter.Matches(run)
                )
                .OrderBy(static run => run.StatusSequence)
                .Take(chunkSize)
                .Select(static run => (run.Id, run.StatusSequence))
                .ToArray();

            var writes = walked.Select(run => _RequeueLocked(run.Id, highWaterMark)).ToArray();

            return (writes, walked.Length < chunkSize ? null : walked[^1].StatusSequence);
        }
    }

    private StoreWrite _Transition(string id, DiagnosticsRunStatus status, string? error)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(id, out var run))
            {
                return StoreWrite.NotFound(id);
            }

            if (run.Status == status)
            {
                return StoreWrite.Skipped(id, "Already " + status.ToString().ToLowerInvariant());
            }

            var now = _timeProvider.GetUtcNow();
            var sequence = ++_sequence;
            var changed = run with
            {
                Status = status,
                Attempts = status == DiagnosticsRunStatus.Running ? run.Attempts + 1 : run.Attempts,
                LastError = status == DiagnosticsRunStatus.Failed ? error : run.LastError,
                UpdatedAt = now,
                StatusChangedAt = now,
                Version = sequence,
                StatusSequence = sequence,
                Timeline =
                [
                    .. run.Timeline,
                    new DiagnosticsRunEvent
                    {
                        Kind = status switch
                        {
                            DiagnosticsRunStatus.Running => DiagnosticsRunEventKind.Started,
                            DiagnosticsRunStatus.Succeeded => DiagnosticsRunEventKind.Succeeded,
                            DiagnosticsRunStatus.Failed => DiagnosticsRunEventKind.Failed,
                            _ => DiagnosticsRunEventKind.Requeued,
                        },
                        At = now,
                        Text = status == DiagnosticsRunStatus.Failed ? error : null,
                    },
                ],
            };
            _runs[id] = changed;

            var affectsAttention = run.Status == DiagnosticsRunStatus.Failed || status == DiagnosticsRunStatus.Failed;

            return StoreWrite.Done(changed, affectsAttention);
        }
    }

    private StoreWrite _RequeueLocked(string id, long highWaterMark)
    {
        if (!_runs.TryGetValue(id, out var run))
        {
            return StoreWrite.NotFound(id);
        }

        if (run.Status == DiagnosticsRunStatus.Running)
        {
            return StoreWrite.Skipped(id, DiagnosticsCommands.RunningReason);
        }

        if (run.Status != DiagnosticsRunStatus.Failed)
        {
            return StoreWrite.Skipped(id, "No longer failed");
        }

        if (run.StatusSequence > highWaterMark)
        {
            return StoreWrite.Skipped(id, "Failed after the Operation was confirmed");
        }

        var now = _timeProvider.GetUtcNow();
        var sequence = ++_sequence;
        var changed = run with
        {
            Status = DiagnosticsRunStatus.Queued,
            UpdatedAt = now,
            StatusChangedAt = now,
            Version = sequence,
            StatusSequence = sequence,
            Timeline = [.. run.Timeline, new DiagnosticsRunEvent { Kind = DiagnosticsRunEventKind.Requeued, At = now }],
        };
        _runs[id] = changed;

        return StoreWrite.Done(changed, affectsAttention: true);
    }

    private static DiagnosticsRun _Expect(StoreWrite write, string id)
    {
        return write.Outcome switch
        {
            DashboardRowOutcome.Done => write.Run!,
            DashboardRowOutcome.Failed => throw new KeyNotFoundException($"No diagnostic run has the id '{id}'."),
            _ => throw new InvalidOperationException($"Diagnostic run '{id}' was not changed: {write.Reason}."),
        };
    }

    // A fixed key in every payload, so the payload formatter has a secret to redact.
    private static string _Payload(long number)
    {
        var text = number.ToString(CultureInfo.InvariantCulture);

        return "{\"input\":{\"number\":" + text + "},\"apiKey\":\"diag-key-" + text + "\"}";
    }
}

/// <summary>A snapshot of one diagnostic run.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DiagnosticsRun
{
    /// <summary>Gets the run's id, such as <c>run-000001</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the run's status.</summary>
    public required DiagnosticsRunStatus Status { get; init; }

    /// <summary>Gets how many times the run started.</summary>
    public required int Attempts { get; init; }

    /// <summary>Gets the error of the run's latest failure, or <see langword="null"/> when it never failed.</summary>
    public string? LastError { get; init; }

    /// <summary>Gets the W3C trace id the run carries.</summary>
    public required string TraceId { get; init; }

    /// <summary>Gets when the run was added.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when the run last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Gets when the run entered its current status.</summary>
    public required DateTimeOffset StatusChangedAt { get; init; }

    /// <summary>Gets the store sequence at which the run was added.</summary>
    public required long CreatedSequence { get; init; }

    /// <summary>Gets the store sequence of the run's latest change: its version on the dashboard's live stream.</summary>
    public required long Version { get; init; }

    /// <summary>Gets the store sequence at which the run entered its current status.</summary>
    public required long StatusSequence { get; init; }

    /// <summary>Gets the run's raw payload, a JSON document that carries a secret the module redacts.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets the run's timeline: when it was added, each start, completion, failure, requeue, and note.</summary>
    public required IReadOnlyList<DiagnosticsRunEvent> Timeline { get; init; }
}

/// <summary>The status of a diagnostic run.</summary>
[PublicAPI]
public enum DiagnosticsRunStatus
{
    /// <summary>The run waits to start.</summary>
    Queued = 0,

    /// <summary>The run is running.</summary>
    Running = 1,

    /// <summary>The run succeeded.</summary>
    Succeeded = 2,

    /// <summary>The run failed.</summary>
    Failed = 3,
}

/// <summary>One entry in a diagnostic run's timeline.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DiagnosticsRunEvent
{
    /// <summary>Gets what happened.</summary>
    public required DiagnosticsRunEventKind Kind { get; init; }

    /// <summary>Gets when it happened.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets the failure's error or the note's text, or <see langword="null"/>.</summary>
    public string? Text { get; init; }
}

/// <summary>What happened to a diagnostic run.</summary>
[PublicAPI]
public enum DiagnosticsRunEventKind
{
    /// <summary>The run was added, queued.</summary>
    Created = 0,

    /// <summary>The run started.</summary>
    Started = 1,

    /// <summary>The run succeeded.</summary>
    Succeeded = 2,

    /// <summary>The run failed.</summary>
    Failed = 3,

    /// <summary>The run was requeued.</summary>
    Requeued = 4,

    /// <summary>An operator added a note.</summary>
    Note = 5,
}

/// <summary>The result of one write to the store: the row outcome, and the changed run when it changed.</summary>
internal sealed record StoreWrite
{
    public required string Id { get; init; }

    public required DashboardRowOutcome Outcome { get; init; }

    public string? Reason { get; init; }

    public DiagnosticsRun? Run { get; init; }

    public bool AffectsAttention { get; init; }

    public DashboardRowResult Row =>
        new()
        {
            Id = Id,
            Outcome = Outcome,
            Reason = Reason,
        };

    public static StoreWrite Done(DiagnosticsRun run, bool affectsAttention)
    {
        return new StoreWrite
        {
            Id = run.Id,
            Outcome = DashboardRowOutcome.Done,
            Run = run,
            AffectsAttention = affectsAttention,
        };
    }

    public static StoreWrite Skipped(string id, string reason)
    {
        return new StoreWrite
        {
            Id = id,
            Outcome = DashboardRowOutcome.Skipped,
            Reason = reason,
        };
    }

    public static StoreWrite NotFound(string id)
    {
        return new StoreWrite
        {
            Id = id,
            Outcome = DashboardRowOutcome.Failed,
            Reason = "Run not found",
        };
    }
}

/// <summary>
/// The filter a list view sends and an Operation freezes: <c>{ "status": "Failed", "error": "..." }</c>, each field
/// optional.
/// </summary>
internal sealed record DiagnosticsRunFilter
{
    public DiagnosticsRunStatus? Status { get; init; }

    public string? Error { get; init; }

    /// <summary>Whether the filter named a status that does not exist, so it matches no run.</summary>
    public bool MatchesNothing { get; init; }

    public bool Matches(DiagnosticsRun run)
    {
        return !MatchesNothing
            && (Status is null || run.Status == Status)
            && (Error is null || string.Equals(run.LastError, Error, StringComparison.Ordinal));
    }

    /// <summary>Reads a filter; a status that names no status matches no run.</summary>
    public static DiagnosticsRunFilter Parse(JsonElement filter)
    {
        if (filter.ValueKind != JsonValueKind.Object)
        {
            return new DiagnosticsRunFilter();
        }

        return Create(_String(filter, "status"), _String(filter, "error"));
    }

    /// <summary>Creates a filter; a status that names no status matches no run.</summary>
    public static DiagnosticsRunFilter Create(string? status, string? error)
    {
        if (string.IsNullOrEmpty(status))
        {
            return new DiagnosticsRunFilter { Error = error };
        }

        // IsDefined: TryParse also accepts any number, such as "99".
        var known =
            Enum.TryParse<DiagnosticsRunStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed);

        return new DiagnosticsRunFilter
        {
            Status = known ? parsed : null,
            Error = error,
            MatchesNothing = !known,
        };
    }

    private static string? _String(JsonElement filter, string name)
    {
        return filter.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
