// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Fencing;
using Headless.Jobs;

namespace Tests.Cluster;

/// <summary>What one enqueued job was asked to do: the policy and budget it was stored with and how its body runs.</summary>
/// <param name="Id">The job id the store assigned.</param>
/// <param name="Index">The workload index the job was enqueued at.</param>
/// <param name="Policy">The stored node-death policy.</param>
/// <param name="Retries">The stored retry budget.</param>
/// <param name="Work">How long the body runs.</param>
/// <param name="IgnoresToken">Whether the body keeps running after its token is cancelled.</param>
public sealed record JobSpec(Guid Id, int Index, NodeDeathPolicy Policy, int Retries, TimeSpan Work, bool IgnoresToken);

/// <summary>How a body ended.</summary>
public enum AttemptEnd
{
    /// <summary>The body ran to its end and returned.</summary>
    Completed,

    /// <summary>The token was cancelled while the node held its membership and was not stopping: a renewal failed.</summary>
    LeaseLost,

    /// <summary>The token was cancelled after the node lost its coordination membership.</summary>
    MembershipLost,

    /// <summary>The token was cancelled by a graceful host stop with membership intact, which charges no retry.</summary>
    GracefulStop,
}

/// <summary>
/// What a token-ignoring body read from its own row just before it returned: whether the run had already lost the row,
/// in which case its completion write must be refused.
/// </summary>
/// <param name="Status">The row's status.</param>
/// <param name="Holder">The lease holder, kept as a record after the lease ends.</param>
/// <param name="Generation">The lease generation.</param>
/// <param name="State">The lease state.</param>
/// <param name="ExpiredOnStoreClock">Whether the lease deadline had passed on the store's clock.</param>
public sealed record RowObservation(
    JobStatus Status,
    string? Holder,
    long Generation,
    RowLeaseState State,
    bool ExpiredOnStoreClock
)
{
    /// <summary>Whether the run that read this no longer held the row, judged against its claim owner.</summary>
    public bool LostBy(string? owner) =>
        _IsTerminal(Status)
        || State != RowLeaseState.Active
        || ExpiredOnStoreClock
        || !string.Equals(Holder, owner, StringComparison.Ordinal);

    private static bool _IsTerminal(JobStatus status) => status >= JobStatus.Succeeded;
}

/// <summary>One invocation of a job body, as the ledger recorded it.</summary>
/// <param name="JobId">The job.</param>
/// <param name="Node">The node slot that ran it.</param>
/// <param name="Incarnation">The host incarnation in that slot.</param>
/// <param name="Owner">The node's owner tag when the body started, which is the claim owner.</param>
/// <param name="RetryCount">The row's retry count the run saw.</param>
/// <param name="StartedAt">When the body started, on the ledger clock.</param>
/// <param name="End">How it ended, or null while it runs or after its incarnation was killed.</param>
/// <param name="EndedAt">When it ended.</param>
/// <param name="Observation">What a token-ignoring body read from its row before returning.</param>
/// <param name="KilledAt">When its incarnation was killed, for a body that was still running then.</param>
public sealed record Attempt(
    Guid JobId,
    string Node,
    int Incarnation,
    string? Owner,
    int RetryCount,
    TimeSpan StartedAt,
    AttemptEnd? End,
    TimeSpan? EndedAt,
    RowObservation? Observation,
    TimeSpan? KilledAt
)
{
    /// <summary>The incarnation's name, such as <c>node-1#2</c>.</summary>
    public string Runner => $"{Node}#{Incarnation.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>When the attempt stopped running as far as the ledger can tell; null while it still runs.</summary>
    public TimeSpan? StoppedAt => EndedAt ?? KilledAt;
}

/// <summary>
/// Every job body start and end across the cluster, on one monotonic clock. A killed incarnation records nothing after
/// its kill instant: its bodies keep running in this process, but a crashed node's would not, so recording them would
/// show runs no real crash produces.
/// </summary>
public sealed class JobLedger
{
    private readonly Lock _lock = new();
    private readonly List<AttemptState> _attempts = [];
    private readonly Dictionary<Guid, JobSpec> _specs = [];
    private readonly HashSet<string> _killed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan> _killedAt = new(StringComparer.Ordinal);

    // wall-clock: the ledger orders events from several in-process hosts on one monotonic clock; no test advances it
    private readonly long _origin = Stopwatch.GetTimestamp();

    /// <summary>The time since the ledger was created.</summary>
    public TimeSpan Now => Stopwatch.GetElapsedTime(_origin);

    /// <summary>Records a job the workload enqueued.</summary>
    public void RecordEnqueued(JobSpec spec)
    {
        lock (_lock)
        {
            _specs[spec.Id] = spec;
        }
    }

    /// <summary>Stops recording for <paramref name="runner"/> from now on.</summary>
    public void MarkKilled(string runner)
    {
        lock (_lock)
        {
            if (_killed.Add(runner))
            {
                _killedAt[runner] = Now;
            }
        }
    }

    /// <summary>Records a body start, or returns null when its incarnation has been killed.</summary>
    public AttemptHandle? Start(Guid jobId, string node, int incarnation, string? owner, int retryCount)
    {
        lock (_lock)
        {
            var state = new AttemptState(jobId, node, incarnation, owner, retryCount, Now);

            if (_killed.Contains(state.Runner))
            {
                return null;
            }

            _attempts.Add(state);
            return new AttemptHandle(this, state);
        }
    }

    /// <summary>A copy of everything recorded so far.</summary>
    public LedgerSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new LedgerSnapshot(
                new Dictionary<Guid, JobSpec>(_specs),
                [
                    .. _attempts.Select(state => new Attempt(
                        state.JobId,
                        state.Node,
                        state.Incarnation,
                        state.Owner,
                        state.RetryCount,
                        state.StartedAt,
                        state.End,
                        state.EndedAt,
                        state.Observation,
                        state.End is null && _killedAt.TryGetValue(state.Runner, out var killedAt) ? killedAt : null
                    )),
                ]
            );
        }
    }

    private void _End(AttemptState state, AttemptEnd end)
    {
        lock (_lock)
        {
            if (_killed.Contains(state.Runner) || state.End is not null)
            {
                return;
            }

            state.End = end;
            state.EndedAt = Now;
        }
    }

    private void _Observe(AttemptState state, RowObservation observation)
    {
        lock (_lock)
        {
            if (!_killed.Contains(state.Runner))
            {
                state.Observation = observation;
            }
        }
    }

    /// <summary>The recorder a running body holds to report how it ended.</summary>
    public sealed class AttemptHandle
    {
        private readonly JobLedger _ledger;
        private readonly AttemptState _state;

        internal AttemptHandle(JobLedger ledger, AttemptState state)
        {
            _ledger = ledger;
            _state = state;
        }

        public void End(AttemptEnd end) => _ledger._End(_state, end);

        public void Observe(RowObservation observation) => _ledger._Observe(_state, observation);
    }

    internal sealed class AttemptState(
        Guid jobId,
        string node,
        int incarnation,
        string? owner,
        int retryCount,
        TimeSpan startedAt
    )
    {
        public Guid JobId { get; } = jobId;

        public string Node { get; } = node;

        public int Incarnation { get; } = incarnation;

        public string? Owner { get; } = owner;

        public int RetryCount { get; } = retryCount;

        public TimeSpan StartedAt { get; } = startedAt;

        public string Runner => $"{Node}#{Incarnation.ToString(CultureInfo.InvariantCulture)}";

        public AttemptEnd? End { get; set; }

        public TimeSpan? EndedAt { get; set; }

        public RowObservation? Observation { get; set; }
    }
}

/// <summary>An immutable copy of a <see cref="JobLedger"/>.</summary>
public sealed record LedgerSnapshot(IReadOnlyDictionary<Guid, JobSpec> Specs, IReadOnlyList<Attempt> Attempts);
