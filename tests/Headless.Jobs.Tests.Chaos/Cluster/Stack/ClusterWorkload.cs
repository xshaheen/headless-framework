// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

namespace Tests.Cluster;

/// <summary>One job the workload asks for, before the store assigns it an id.</summary>
public sealed record WorkItem(int Index, NodeDeathPolicy Policy, int Retries, TimeSpan Work, bool IgnoresToken)
{
    public ClusterWork Request(bool doubleRun = false) =>
        new(Index, (int)Work.TotalMilliseconds, IgnoresToken, doubleRun);

    public JobOptions Options => new() { OnNodeDeath = Policy, Retries = Retries };

    public JobSpec Spec(Guid id) => new(id, Index, Policy, Retries, Work, IgnoresToken);
}

/// <summary>
/// The jobs a run enqueues, drawn from its seed: half <c>Retry</c> with a budget of one to three, a quarter each
/// <c>Skip</c> and <c>MarkFailed</c>. Most bodies are short, some run long enough to span a fault, and a few ignore
/// their token and run past lease loss, so a late completion has something to be refused.
/// </summary>
public sealed class ClusterWorkload(int seed)
{
    private readonly Random _random = new(seed);
    private readonly Lock _lock = new();
    private int _next;

    public WorkItem Next()
    {
        lock (_lock)
        {
            var index = _next++;
            var roll = _random.NextDouble();
            var policy =
                roll < 0.5 ? NodeDeathPolicy.Retry
                : roll < 0.75 ? NodeDeathPolicy.Skip
                : NodeDeathPolicy.MarkFailed;
            var retries = policy == NodeDeathPolicy.Retry ? _random.Next(1, 4) : 0;
            var shape = _random.NextDouble();

            return shape switch
            {
                < 0.05 => new WorkItem(index, policy, retries, _Seconds(30, 45), IgnoresToken: true),
                < 0.15 => new WorkItem(index, policy, retries, _Seconds(15, 25), IgnoresToken: false),
                < 0.35 => new WorkItem(index, policy, retries, _Seconds(3, 8), IgnoresToken: false),
                _ => new WorkItem(index, policy, retries, _Seconds(0.2, 2), IgnoresToken: false),
            };
        }
    }

    /// <summary>A long job of <paramref name="policy"/> that honors or ignores its token, for a scenario's opening burst.</summary>
    public WorkItem Long(NodeDeathPolicy policy, bool ignoresToken)
    {
        lock (_lock)
        {
            return new WorkItem(
                _next++,
                policy,
                policy == NodeDeathPolicy.Retry ? 2 : 0,
                ignoresToken ? _Seconds(50, 55) : _Seconds(25, 35),
                ignoresToken
            );
        }
    }

    private TimeSpan _Seconds(double min, double max) =>
        TimeSpan.FromSeconds(min + ((max - min) * _random.NextDouble()));
}
