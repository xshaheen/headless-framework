// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Coordination;
using Headless.Jobs.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

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

    [Fact]
    public async Task should_warn_once_naming_each_digest_and_its_nodes_after_an_hour_of_one_disagreement()
    {
        var fleet = new Fleet(
            _Sweeping(_Identity("p1", 1), _OtherDigest),
            _Sweeping(_Identity("p2", 1), _OtherDigest),
            _Sweeping(_Identity("p3", 1), "digest-d3")
        );

        await fleet.CheckAsync();
        fleet.Time.Advance(TimeSpan.FromMinutes(59));
        await fleet.CheckAsync();
        fleet.Warnings.Should().BeEmpty("an hour has not passed");

        fleet.Time.Advance(TimeSpan.FromMinutes(2));
        await fleet.CheckAsync();
        fleet.Time.Advance(TimeSpan.FromHours(3));
        await fleet.CheckAsync();

        var warning = fleet.Warnings.Should().ContainSingle("one warning per episode").Subject;
        warning
            .Should()
            .Contain($"{_OtherDigest}: {_Identity("p1", 1)}, {_Identity("p2", 1)}")
            .And.Contain($"digest-d3: {_Identity("p3", 1)}")
            .And.Contain(_LocalDigest);
    }

    [Fact]
    public async Task should_start_a_new_episode_after_an_agreed_pass()
    {
        var disagreeing = _Sweeping(_Identity("p1", 1), _OtherDigest);
        var fleet = new Fleet(disagreeing);

        await fleet.CheckAsync();
        fleet.Time.Advance(TimeSpan.FromMinutes(61));
        await fleet.CheckAsync();
        fleet.Snapshot = [];
        await fleet.CheckAsync();
        fleet.Snapshot = [disagreeing];
        await fleet.CheckAsync();
        fleet.Time.Advance(TimeSpan.FromMinutes(30));
        await fleet.CheckAsync();
        fleet.Warnings.Should().ContainSingle("the second episode is only 30 minutes old");

        fleet.Time.Advance(TimeSpan.FromMinutes(31));
        await fleet.CheckAsync();

        fleet.Warnings.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_start_a_new_episode_when_a_disagreeing_peer_restarts_as_a_new_incarnation()
    {
        var fleet = new Fleet(_Sweeping(_Identity("p1", 1), _OtherDigest));

        await fleet.CheckAsync();
        fleet.Time.Advance(TimeSpan.FromMinutes(50));
        fleet.Snapshot = [_Sweeping(_Identity("p1", 2), _OtherDigest)];
        await fleet.CheckAsync();
        fleet.Time.Advance(TimeSpan.FromMinutes(20));
        await fleet.CheckAsync();
        fleet.Warnings.Should().BeEmpty("the restarted incarnation has disagreed for only 20 minutes");

        fleet.Time.Advance(TimeSpan.FromMinutes(41));
        await fleet.CheckAsync();

        fleet.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task should_neither_start_nor_end_an_episode_when_the_snapshot_cannot_be_read()
    {
        var disagreeing = _Sweeping(_Identity("p1", 1), _OtherDigest);
        var fleet = new Fleet(disagreeing) { Failure = new InvalidOperationException("store down") };

        // An unreadable snapshot names no node, so an hour of it is not an hour of disagreement.
        await fleet.CheckAsync();
        fleet.Time.Advance(TimeSpan.FromMinutes(61));
        fleet.Failure = null;
        await fleet.CheckAsync();
        fleet.Warnings.Should().BeEmpty("the episode began at the first readable disagreement");

        // And it does not end one: the episode that just began keeps its start.
        fleet.Time.Advance(TimeSpan.FromMinutes(30));
        fleet.Failure = new InvalidOperationException("store down");
        await fleet.CheckAsync();
        fleet.Time.Advance(TimeSpan.FromMinutes(31));
        fleet.Failure = null;
        await fleet.CheckAsync();

        fleet.Warnings.Should().ContainSingle();
    }

    /// <summary>A gate over a fleet whose snapshot and clock a test drives, recording the warnings it logs.</summary>
    private sealed class Fleet
    {
        private readonly CapturingLogger _logger = new();
        private readonly CronFleetAgreement _agreement;

        public Fleet(params NodeLivenessSnapshot[] snapshot)
        {
            Snapshot = snapshot;
            var membership = Substitute.For<INodeMembership>();
            membership
                .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                    Failure is null
                        ? new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>(Snapshot)
                        : ValueTask.FromException<IReadOnlyList<NodeLivenessSnapshot>>(Failure)
                );
            _agreement = _Create(membership, Time, _logger);
        }

        public FakeTimeProvider Time { get; } = new();

        public IReadOnlyList<NodeLivenessSnapshot> Snapshot { get; set; }

        public Exception? Failure { get; set; }

        public IReadOnlyList<string> Warnings => _logger.Holdouts;

        public Task CheckAsync() => _agreement.CheckAsync(AbortToken);
    }

    private sealed class CapturingLogger : ILogger<CronFleetAgreement>
    {
        // The holdout warning's event id; the snapshot-unavailable warning is a different event.
        private const int _HoldoutEventId = 3233;
        private readonly List<string> _holdouts = [];

        public IReadOnlyList<string> Holdouts => _holdouts;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (eventId.Id == _HoldoutEventId)
            {
                _holdouts.Add(formatter(state, exception));
            }
        }
    }

    private static CronFleetAgreement _Create(
        INodeMembership membership,
        TimeProvider? time = null,
        ILogger<CronFleetAgreement>? logger = null
    )
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
            time ?? TimeProvider.System,
            logger ?? NullLogger<CronFleetAgreement>.Instance
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
