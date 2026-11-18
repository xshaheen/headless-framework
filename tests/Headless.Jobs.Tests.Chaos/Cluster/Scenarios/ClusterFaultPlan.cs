// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests.Cluster;

/// <summary>A fault the cluster loop can inject on one node.</summary>
public enum ClusterFaultKind
{
    /// <summary>Cuts both of the node's routes and abandons its host; a replacement starts after the fault's length.</summary>
    Kill,

    /// <summary>Black-holes both routes' replies for longer than the lease, then lets traffic through again.</summary>
    Freeze,

    /// <summary>Cuts only the coordination route for the fault's length; the Jobs store stays reachable.</summary>
    CoordinationPartition,

    /// <summary>Cuts both routes for the fault's length, then restores them.</summary>
    FullPartition,

    /// <summary>Stops the node gracefully and starts a replacement.</summary>
    GracefulRestart,
}

/// <summary>One fault: when it starts, what it does, how long it lasts, and which node it hits.</summary>
public sealed record ClusterFault(TimeSpan At, ClusterFaultKind Kind, TimeSpan Length, int Node)
{
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{At.TotalSeconds:F1}s {Kind} node-{Node}{(Kind == ClusterFaultKind.GracefulRestart ? string.Empty : $" {Length.TotalSeconds:F1}s")}"
        );
}

/// <summary>
/// The faults one cluster run injects, picked from its seed alone, so a failed run that printed its seed replays the
/// same sequence. Faults never overlap: each sits in its own equal slot and ends before the next slot begins.
/// </summary>
public sealed record ClusterFaultPlan(int Seed, IReadOnlyList<ClusterFault> Faults)
{
    private static readonly TimeSpan _ShortestFault = TimeSpan.FromSeconds(2);

    /// <summary>Builds a plan over <paramref name="duration"/>.</summary>
    /// <param name="seed">The seed every choice derives from.</param>
    /// <param name="duration">How long faults are injected.</param>
    /// <param name="faults">How many faults to inject.</param>
    /// <param name="nodes">How many nodes the faults pick from.</param>
    /// <param name="freeze">How long a freeze lasts; it must outlast the lease, so it is not drawn at random.</param>
    /// <param name="kinds">The kinds to draw from; every kind when omitted.</param>
    public static ClusterFaultPlan Create(
        int seed,
        TimeSpan duration,
        int faults,
        int nodes,
        TimeSpan freeze,
        IReadOnlyList<ClusterFaultKind>? kinds = null
    )
    {
        var random = new Random(seed);
        var pool = kinds ?? Enum.GetValues<ClusterFaultKind>();
        var slot = duration / faults;
        var longest = TimeSpan.FromTicks((long)(slot.Ticks * 0.45));
        var plan = new List<ClusterFault>(faults);

        for (var i = 0; i < faults; i++)
        {
            var kind = pool[random.Next(pool.Count)];
            var node = random.Next(nodes);
            var drawn = _ShortestFault + ((longest - _ShortestFault) * random.NextDouble());
            var length =
                kind == ClusterFaultKind.Freeze ? freeze
                : drawn < _ShortestFault ? _ShortestFault
                : drawn;
            var latestStart = slot - length;
            var at =
                (slot * i) + (latestStart > TimeSpan.Zero ? latestStart * 0.5 * random.NextDouble() : TimeSpan.Zero);

            plan.Add(new ClusterFault(at, kind, length, node));
        }

        return new ClusterFaultPlan(seed, plan);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"seed {Seed}: ") + string.Join("; ", Faults);
}
