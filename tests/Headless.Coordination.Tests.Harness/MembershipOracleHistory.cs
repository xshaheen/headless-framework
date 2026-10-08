// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Coordination;

namespace Tests;

/// <summary>A provider whose stored membership the oracle can read raw and move through time.</summary>
public interface ICoordinationOracleFixture : ICoordinationFixture
{
    /// <summary>
    /// Moves every instant stored for <paramref name="nodeId" /> in <paramref name="clusterName" /> by
    /// <paramref name="delta" />: back to age it, forward to stand for a database clock that stepped back.
    /// </summary>
    Task ShiftAsync(string clusterName, string nodeId, TimeSpan delta, CancellationToken cancellationToken);

    /// <summary>Reads the cluster's rows directly from the tables, bypassing the store.</summary>
    Task<StoredMembership> ReadRowsAsync(string clusterName, CancellationToken cancellationToken);

    /// <summary>
    /// Makes <paramref name="store" /> delete retention-expired rows on every snapshot read instead of at its
    /// production cadence, so the stored rows can be compared with the model after each step.
    /// </summary>
    void PruneOnEverySnapshot(IMembershipStore store);
}

/// <summary>The rows one cluster holds, read raw.</summary>
public sealed record StoredMembership(
    IReadOnlyList<(string NodeId, long CurrentIncarnation)> Generations,
    IReadOnlyList<(string NodeId, long Incarnation, bool Left)> Liveness,
    IReadOnlyList<(string NodeId, long Incarnation)> Descriptors
)
{
    /// <summary>Renders the rows in ordinal order, so two stores holding the same rows render the same text.</summary>
    public string Describe(Func<string, string> node)
    {
        var generations = Generations
            .Select(g => string.Create(CultureInfo.InvariantCulture, $"{node(g.NodeId)}={g.CurrentIncarnation}"))
            .Order(StringComparer.Ordinal);
        var liveness = Liveness
            .Select(l =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{node(l.NodeId)}@{l.Incarnation}{(l.Left ? "+left" : "")}"
                )
            )
            .Order(StringComparer.Ordinal);
        var descriptors = Descriptors
            .Select(d => string.Create(CultureInfo.InvariantCulture, $"{node(d.NodeId)}@{d.Incarnation}"))
            .Order(StringComparer.Ordinal);

        return $"gen[{string.Join(' ', generations)}] live[{string.Join(' ', liveness)}] desc[{string.Join(' ', descriptors)}]";
    }
}

/// <summary>One membership-store call, or a move of stored time, in a generated history.</summary>
/// <remarks>
/// A slot names an incarnation relative to what the history allocated for the node: 0 the latest (1 when none was
/// allocated), 1 the one before it, 2 one past the latest, which no store has issued.
/// </remarks>
public abstract record MembershipOracleOp
{
    public sealed record Allocate(int Node) : MembershipOracleOp;

    public sealed record Register(int Node, int Slot, int Descriptor) : MembershipOracleOp;

    public sealed record Heartbeat(int Node, int Slot) : MembershipOracleOp;

    public sealed record Leave(int Node, int Slot) : MembershipOracleOp;

    public sealed record ReadSnapshot : MembershipOracleOp;

    public sealed record ReadNode(int Node, int Slot) : MembershipOracleOp;

    public sealed record ReadLive : MembershipOracleOp;

    /// <summary>Ages every node's stored instants by <see cref="By" />.</summary>
    public sealed record AdvanceTime(TimeSpan By) : MembershipOracleOp;

    /// <summary>Moves one node's stored instants into the future, as if the database clock had stepped back.</summary>
    public sealed record Skew(int Node, TimeSpan By) : MembershipOracleOp;

    // Sealed so the derived records keep this rendering instead of synthesizing their own.
    public sealed override string ToString()
    {
        return this switch
        {
            Allocate a => $"allocate n{a.Node}",
            Register r => $"register n{r.Node}/s{r.Slot} d{r.Descriptor}",
            Heartbeat h => $"heartbeat n{h.Node}/s{h.Slot}",
            Leave l => $"leave n{l.Node}/s{l.Slot}",
            ReadSnapshot => "snapshot",
            ReadNode n => $"read n{n.Node}/s{n.Slot}",
            ReadLive => "live",
            AdvanceTime t => string.Create(CultureInfo.InvariantCulture, $"advance {t.By.TotalSeconds}s"),
            Skew s => string.Create(CultureInfo.InvariantCulture, $"skew n{s.Node} +{s.By.TotalSeconds}s"),
            _ => GetType().Name,
        };
    }
}

/// <summary>A generated history: the node ids it uses and the operations over them.</summary>
public sealed record MembershipOracleHistory(
    int Seed,
    IReadOnlyList<string> Nodes,
    IReadOnlyList<MembershipOracleOp> Ops
)
{
    public MembershipOracleHistory With(IReadOnlyList<MembershipOracleOp> ops) => this with { Ops = ops };

    public string Describe()
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"    nodes: {string.Join(", ", Nodes.Select((n, i) => $"n{i}={MembershipOracleGenerator.Escape(n)}"))}"
        );

        for (var i = 0; i < Ops.Count; i++)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"    {i}: {Ops[i]}");
        }

        return builder.ToString();
    }
}

/// <summary>
/// Generates membership histories from a seed with <see cref="Random" /> alone, so any history is replayed exactly by
/// its seed.
/// </summary>
/// <remarks>
/// <para>
/// Every stored instant the history moves is moved by a whole multiple of 30 seconds, and every classification
/// boundary sits 15 seconds off such a multiple: suspicion at 75 s, death at 135 s, and pruning at 255 s (death plus a
/// 120 s retention window). An instant's age as the database sees it is therefore its model age plus the real time
/// elapsed since it was written, and it lands on the model's side of every boundary as long as the history runs in
/// under 15 seconds of real time; the oracle refuses a history that takes longer than <see cref="MaxRealTime" />.
/// </para>
/// </remarks>
public static class MembershipOracleGenerator
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan SuspicionThreshold = TimeSpan.FromSeconds(75);
    public static readonly TimeSpan DeadThreshold = TimeSpan.FromSeconds(135);
    public static readonly TimeSpan DeadRetentionWindow = TimeSpan.FromSeconds(120);

    /// <summary>The longest real time a history may take and still keep a 5-second margin on every boundary.</summary>
    public static readonly TimeSpan MaxRealTime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Node ids every store keeps apart: case, accent composition, and a surrogate pair. None contains <c>@</c>, which
    /// separates the node id from the incarnation in an identity's text.
    /// </summary>
    internal static readonly string[] NodePool =
    [
        "node-a",
        "Node-A",
        "node-b",
        "caf\u00e9",
        "cafe\u0301",
        "n\U0001F600",
    ];

    private static readonly TimeSpan[] _Advances =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(150),
        TimeSpan.FromSeconds(300),
    ];

    private static readonly TimeSpan[] _Skews = [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    public static MembershipOracleHistory Generate(int seed, int length = 30)
    {
        var random = new Random(seed);
        var nodes = NodePool.OrderBy(_ => random.Next()).Take(random.Next(2, 5)).ToList();
        var ops = new List<MembershipOracleOp>(length);

        for (var i = 0; i < length; i++)
        {
            ops.Add(_NextOp(random, nodes.Count));
        }

        return new MembershipOracleHistory(seed, nodes, ops);
    }

    private static MembershipOracleOp _NextOp(Random random, int nodeCount)
    {
        var node = random.Next(nodeCount);

        return random.Next(100) switch
        {
            < 14 => new MembershipOracleOp.Allocate(node),
            < 30 => new MembershipOracleOp.Register(node, _Slot(random), random.Next(3)),
            < 48 => new MembershipOracleOp.Heartbeat(node, _Slot(random)),
            < 54 => new MembershipOracleOp.Leave(node, _Slot(random)),
            < 64 => new MembershipOracleOp.ReadSnapshot(),
            < 72 => new MembershipOracleOp.ReadNode(node, _Slot(random)),
            < 78 => new MembershipOracleOp.ReadLive(),
            < 94 => new MembershipOracleOp.AdvanceTime(_Advances[random.Next(_Advances.Length)]),
            _ => new MembershipOracleOp.Skew(node, _Skews[random.Next(_Skews.Length)]),
        };
    }

    // Mostly the latest incarnation, sometimes the superseded one, rarely one never issued.
    private static int _Slot(Random random)
    {
        var roll = random.Next(10);

        return roll < 7 ? 0
            : roll < 9 ? 1
            : 2;
    }

    internal static string Escape(string value)
    {
        var builder = new StringBuilder("\"");

        foreach (var c in value)
        {
            builder.Append(c is < ' ' or > '~' ? $"\\u{(int)c:x4}" : c.ToString());
        }

        return builder.Append('"').ToString();
    }
}
