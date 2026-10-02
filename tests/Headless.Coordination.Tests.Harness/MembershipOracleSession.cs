// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs one history against a real store through <see cref="IMembershipStore" />, in a cluster of its own, and reports
/// each step the way <see cref="MembershipOracleModel" /> does.
/// </summary>
public sealed class MembershipOracleSession : IMembershipOracleTarget
{
    private readonly ICoordinationOracleFixture _fixture;
    private readonly MembershipOracleScope _scope;
    private readonly CoordinationNodeHandle _host;
    private readonly IMembershipStore _store;
    private readonly string _cluster;

    private MembershipOracleSession(
        ICoordinationOracleFixture fixture,
        MembershipOracleScope scope,
        CoordinationNodeHandle host,
        string cluster
    )
    {
        _fixture = fixture;
        _scope = scope;
        _host = host;
        _store = host.Services.GetRequiredService<IMembershipStore>();
        _cluster = cluster;
    }

    public static async Task<MembershipOracleSession> StartAsync(
        ICoordinationOracleFixture fixture,
        MembershipOracleScope scope,
        CancellationToken cancellationToken
    )
    {
        // A cluster per run: every statement is scoped by cluster name, so runs never see each other's rows.
        var cluster = "oracle-" + Guid.NewGuid().ToString("N");
        var host = await fixture
            .CreateNodeAsync(
                cluster,
                "oracle-host",
                MembershipLostBehavior.StopMembershipOnly,
                schema: null,
                static options =>
                {
                    options.HeartbeatInterval = MembershipOracleGenerator.HeartbeatInterval;
                    options.SuspicionThreshold = MembershipOracleGenerator.SuspicionThreshold;
                    options.DeadThreshold = MembershipOracleGenerator.DeadThreshold;
                    options.DeadRetentionWindow = MembershipOracleGenerator.DeadRetentionWindow;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return new MembershipOracleSession(fixture, scope, host, cluster);
    }

    public async Task<string> ExecuteAsync(MembershipOracleOp op, CancellationToken cancellationToken)
    {
        string outcome;

        try
        {
            outcome = op switch
            {
                MembershipOracleOp.Allocate a => await _AllocateAsync(a.Node, cancellationToken).ConfigureAwait(false),
                MembershipOracleOp.Register r => await _RegisterAsync(r, cancellationToken).ConfigureAwait(false),
                MembershipOracleOp.Heartbeat h => "heartbeat:"
                    + (
                        await _store
                            .HeartbeatAsync(_scope.Identity(h.Node, h.Slot), cancellationToken)
                            .ConfigureAwait(false)
                    ).ToString(CultureInfo.InvariantCulture),
                MembershipOracleOp.Leave l => await _LeaveAsync(l, cancellationToken).ConfigureAwait(false),
                MembershipOracleOp.ReadSnapshot => "snapshot:["
                    + _scope.Describe(await _store.ReadLivenessAsync(cancellationToken).ConfigureAwait(false))
                    + "]",
                MembershipOracleOp.ReadNode n => "read:"
                    + (
                        (
                            await _store
                                .ReadNodeLivenessAsync(_scope.Identity(n.Node, n.Slot), cancellationToken)
                                .ConfigureAwait(false)
                        )?.ToString() ?? "absent"
                    ),
                MembershipOracleOp.ReadLive => "live:["
                    + string.Join(
                        ' ',
                        (await _store.ReadLiveNodesAsync(cancellationToken).ConfigureAwait(false)).Select(
                            _scope.Describe
                        )
                    )
                    + "]",
                MembershipOracleOp.AdvanceTime t => await _ShiftAsync(_scope.History.Nodes, -t.By, cancellationToken)
                    .ConfigureAwait(false),
                MembershipOracleOp.Skew s => await _ShiftAsync([_scope.History.Nodes[s.Node]], s.By, cancellationToken)
                    .ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown operation {op}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            outcome = "threw:" + _Describe(e);
        }

        var rows = await _fixture.ReadRowsAsync(_cluster, cancellationToken).ConfigureAwait(false);

        return $"{outcome} | {rows.Describe(_scope.Name)}";
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private async Task<string> _AllocateAsync(int node, CancellationToken cancellationToken)
    {
        var incarnation = await _store
            .AllocateIncarnationAsync(new NodeId(_scope.History.Nodes[node]), cancellationToken)
            .ConfigureAwait(false);
        _scope.Allocated(node, incarnation.Value);

        return string.Create(CultureInfo.InvariantCulture, $"allocate:{incarnation.Value}");
    }

    private async Task<string> _RegisterAsync(MembershipOracleOp.Register op, CancellationToken cancellationToken)
    {
        var (role, metadata) = MembershipOracleScope.Descriptors[op.Descriptor];
        var descriptor = new NodeDescriptor
        {
            Identity = _scope.Identity(op.Node, op.Slot),
            HostName = "oracle-host",
            Role = role,
            Metadata = metadata,
        };

        await _store.UpsertDescriptorAsync(descriptor, cancellationToken).ConfigureAwait(false);

        return "register";
    }

    private async Task<string> _LeaveAsync(MembershipOracleOp.Leave op, CancellationToken cancellationToken)
    {
        await _store.LeaveAsync(_scope.Identity(op.Node, op.Slot), cancellationToken).ConfigureAwait(false);

        return "leave";
    }

    private async Task<string> _ShiftAsync(
        IReadOnlyList<string> nodes,
        TimeSpan delta,
        CancellationToken cancellationToken
    )
    {
        foreach (var node in nodes)
        {
            await _fixture.ShiftAsync(_cluster, node, delta, cancellationToken).ConfigureAwait(false);
        }

        return delta < TimeSpan.Zero ? "advance" : "skew";
    }

    private static string _Describe(Exception e)
    {
        // Read by name so the harness stays free of driver references: Npgsql's SqlState, SqlClient's Number.
        var code = e.GetType().GetProperty("SqlState")?.GetValue(e) ?? e.GetType().GetProperty("Number")?.GetValue(e);

        return code is null
            ? e.GetType().Name
            : $"{e.GetType().Name}({Convert.ToString(code, CultureInfo.InvariantCulture)})";
    }
}
