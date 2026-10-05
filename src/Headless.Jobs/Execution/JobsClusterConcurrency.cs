// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;

namespace Headless.Jobs;

/// <summary>
/// The cluster-wide concurrency limit of each registered job that has one. Persistence providers apply it on every
/// claim: a job's live leased runs (<c>Queued</c> or <c>InProgress</c> with an unexpired lease, across time jobs and
/// cron occurrences) never exceed its limit, so a claimed run holds its slot until it finishes or its lease lapses.
/// </summary>
/// <remarks>
/// Immediate acquisition skips limited jobs and leaves their rows to the scheduler's claim, which is the one place the
/// limit is counted. A non-timed chain descendant runs under its root's claim and is neither counted nor gated.
/// </remarks>
internal sealed class JobsClusterConcurrency
{
    /// <summary>No job has a cluster-wide limit.</summary>
    public static readonly JobsClusterConcurrency None = new(FrozenDictionary<string, int>.Empty);

    private readonly FrozenDictionary<string, int> _limits;

    private JobsClusterConcurrency(FrozenDictionary<string, int> limits)
    {
        _limits = limits;
        LimitedFunctions = [.. limits.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>Whether any job has a cluster-wide limit.</summary>
    public bool HasLimits => LimitedFunctions.Length != 0;

    /// <summary>
    /// The limited job identities in ordinal order. Callers lock them in this order, so two claims that need
    /// overlapping sets of locks never wait on each other in a cycle.
    /// </summary>
    public string[] LimitedFunctions { get; }

    /// <summary>Whether <paramref name="function"/> has a cluster-wide limit.</summary>
    public bool IsLimited(string function) => _limits.ContainsKey(function);

    /// <summary>The cluster-wide limit of <paramref name="function"/>, or <c>0</c> when it has none.</summary>
    public int LimitOf(string function) => _limits.GetValueOrDefault(function);

    public static JobsClusterConcurrency Create(IReadOnlyDictionary<string, JobFunctionRegistration> functions)
    {
        var limits = functions
            .Where(x => x.Value.ClusterMaxConcurrency > 0)
            .ToFrozenDictionary(x => x.Key, x => x.Value.ClusterMaxConcurrency, StringComparer.Ordinal);

        return limits.Count == 0 ? None : new JobsClusterConcurrency(limits);
    }

    /// <summary>
    /// Free slots per limited function, given each function's live leased count. A function absent from
    /// <paramref name="liveCounts"/> has no live runs.
    /// </summary>
    public Dictionary<string, int> FreeSlots(IEnumerable<string> functions, IReadOnlyDictionary<string, int> liveCounts)
    {
        var free = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            if (_limits.TryGetValue(function, out var limit))
            {
                free[function] = Math.Max(0, limit - liveCounts.GetValueOrDefault(function));
            }
        }

        return free;
    }
}
