// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Coordination;
using Headless.Jobs.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The fleet agreement gate: a fingerprint rebase may run only when every live node that runs the sweep published the
/// same cron rules digest as this node.
/// </summary>
public sealed class CronFleetAgreementTests : TestBase
{
    private const string _LocalDigest = "digest-d1";
    private const string _OtherDigest = "digest-d2";
    private static readonly NodeIdentity _Self = _Identity("self", 1);

    [Fact]
    public async Task should_agree_in_a_fleet_of_one()
    {
        var agreement = _Create([_Sweeping(_Self, _LocalDigest)]);

        var result = await agreement.CheckAsync(AbortToken);

        result.Outcome.Should().Be(CronFleetAgreementOutcome.Agreed);
    }

    [Fact]
    public async Task should_agree_when_this_nodes_own_row_is_missing_from_the_snapshot()
    {
        var agreement = _Create([_Sweeping(_Identity("peer", 1), _LocalDigest)]);

        var result = await agreement.CheckAsync(AbortToken);

        result.Outcome.Should().Be(CronFleetAgreementOutcome.Agreed);
    }

    [Fact]
    public async Task should_disagree_and_name_a_sweeping_peer_that_published_another_digest()
    {
        var peer = _Identity("peer", 3);
        var agreement = _Create([_Sweeping(_Self, _LocalDigest), _Sweeping(peer, _OtherDigest)]);

        var result = await agreement.CheckAsync(AbortToken);

        result.Outcome.Should().Be(CronFleetAgreementOutcome.Disagreed);
        result.DisagreeingPeers.Should().Equal(new CronFleetPeer(peer, _OtherDigest));
    }

    [Fact]
    public async Task should_count_a_suspected_peer_but_not_a_dead_one()
    {
        var suspected = _Create([_Sweeping(_Identity("peer", 1), _OtherDigest, NodeLivenessState.Suspected)]);
        var dead = _Create([_Sweeping(_Identity("peer", 1), _OtherDigest, NodeLivenessState.Dead)]);

        (await suspected.CheckAsync(AbortToken))
            .Outcome.Should()
            .Be(CronFleetAgreementOutcome.Disagreed, "a suspected node may still be sweeping");
        (await dead.CheckAsync(AbortToken))
            .Outcome.Should()
            .Be(CronFleetAgreementOutcome.Agreed, "a dead node sweeps nothing");
    }

    [Fact]
    public async Task should_not_count_a_messaging_only_node_or_a_jobs_host_that_never_sweeps()
    {
        var messagingOnly = new NodeLivenessSnapshot(
            _Identity("messaging", 1),
            NodeLivenessState.Alive,
            Role: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
        var apiOnly = new NodeLivenessSnapshot(
            _Identity("api", 1),
            NodeLivenessState.Alive,
            Role: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CronRulesDigest.DigestMetadataKey] = "digest-old",
                [CronRulesDigest.FingerprintSweepMetadataKey] = "false",
            }
        );
        var agreement = _Create([messagingOnly, apiOnly, _Sweeping(_Identity("worker", 1), _LocalDigest)]);

        var result = await agreement.CheckAsync(AbortToken);

        result.Outcome.Should().Be(CronFleetAgreementOutcome.Agreed);
    }

    [Fact]
    public async Task should_report_unavailable_when_the_snapshot_read_throws()
    {
        var membership = Substitute.For<INodeMembership>();
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<NodeLivenessSnapshot>>>(_ =>
                throw new InvalidOperationException("store down")
            );

        var result = await _Create(membership).CheckAsync(AbortToken);

        result.Outcome.Should().Be(CronFleetAgreementOutcome.Unavailable);
    }

    [Fact]
    public async Task should_report_unavailable_when_the_snapshot_read_times_out()
    {
        // The bounded read surfaces as a cancellation the caller did not ask for.
        var membership = Substitute.For<INodeMembership>();
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<NodeLivenessSnapshot>>>(_ => throw new OperationCanceledException());

        var result = await _Create(membership).CheckAsync(AbortToken);

        result.Outcome.Should().Be(CronFleetAgreementOutcome.Unavailable);
    }

    [Fact]
    public async Task should_propagate_the_callers_cancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var membership = Substitute.For<INodeMembership>();
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<NodeLivenessSnapshot>>>(call =>
                throw new OperationCanceledException(call.Arg<CancellationToken>())
            );

        var act = () => _Create(membership).CheckAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static CronFleetAgreement _Create(IReadOnlyList<NodeLivenessSnapshot> snapshot)
    {
        var membership = Substitute.For<INodeMembership>();
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>(snapshot));

        return _Create(membership);
    }

    private static CronFleetAgreement _Create(INodeMembership membership)
    {
        var options = new CoordinationOptions();
        options.Metadata[CronRulesDigest.DigestMetadataKey] = _LocalDigest;
        options.Metadata[CronRulesDigest.FingerprintSweepMetadataKey] = "true";
        var owner = Substitute.For<IJobsOwnerIdentity>();
        owner
            .TryGetStampOwner(out Arg.Any<string?>())
            .Returns(call =>
            {
                call[0] = _Self.ToString();
                return true;
            });

        return new CronFleetAgreement(
            membership,
            Options.Create(options),
            owner,
            NullLogger<CronFleetAgreement>.Instance
        );
    }

    private static NodeLivenessSnapshot _Sweeping(
        NodeIdentity identity,
        string digest,
        NodeLivenessState state = NodeLivenessState.Alive
    )
    {
        return new NodeLivenessSnapshot(
            identity,
            state,
            Role: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CronRulesDigest.DigestMetadataKey] = digest,
                [CronRulesDigest.FingerprintSweepMetadataKey] = "true",
            }
        );
    }

    private static NodeIdentity _Identity(string nodeId, long incarnation)
    {
        return new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation));
    }
}
