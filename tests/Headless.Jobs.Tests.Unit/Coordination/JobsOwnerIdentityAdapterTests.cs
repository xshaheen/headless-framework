// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Coordination;

namespace Tests.Coordination;

public sealed class JobsOwnerIdentityAdapterTests
{
    private static NodeIdentity _Identity(string node, long incarnation)
    {
        return new(new NodeId(node), new NodeIncarnation(incarnation));
    }

    [Fact]
    public void should_expose_node_at_incarnation_when_registered()
    {
        var membership = new FakeNodeMembership { Identity = _Identity("node-a", 5) };
        var options = new SchedulerOptionsBuilder { NodeId = "machine-x" };
        var adapter = new JobsOwnerIdentityAdapter(membership, options);

        adapter.DisplayOwner.Should().Be("node-a@5");
        adapter.TryGetStampOwner(out var owner).Should().BeTrue();
        owner.Should().Be("node-a@5");
    }

    [Fact]
    public void should_refuse_stamp_and_fall_back_display_when_identity_null()
    {
        var membership = new FakeNodeMembership { Identity = null };
        var options = new SchedulerOptionsBuilder { NodeId = "machine-x" };
        var adapter = new JobsOwnerIdentityAdapter(membership, options);

        adapter.TryGetStampOwner(out var owner).Should().BeFalse();
        owner.Should().BeNull();

        // DisplayOwner must never throw on the logging/telemetry hot path; it returns the safe fallback.
        var display = adapter.DisplayOwner;
        display.Should().Be("machine-x");
    }

    [Fact]
    public void should_reflect_identity_transitions_without_caching_a_stale_owner()
    {
        var membership = new FakeNodeMembership { Identity = null };
        var options = new SchedulerOptionsBuilder { NodeId = "machine-x" };
        var adapter = new JobsOwnerIdentityAdapter(membership, options);

        adapter.TryGetStampOwner(out _).Should().BeFalse();

        membership.Identity = _Identity("node-a", 7);
        adapter.TryGetStampOwner(out var afterRegister).Should().BeTrue();
        afterRegister.Should().Be("node-a@7");
        adapter.DisplayOwner.Should().Be("node-a@7");

        membership.Identity = null;
        adapter.TryGetStampOwner(out _).Should().BeFalse();
        adapter.DisplayOwner.Should().Be("machine-x");
    }

    [Fact]
    public void should_surface_membership_lost_token_from_substrate()
    {
        using var cts = new CancellationTokenSource();
        var membership = new FakeNodeMembership { Identity = _Identity("node-a", 5), LostToken = cts.Token };
        var options = new SchedulerOptionsBuilder { NodeId = "machine-x" };
        var adapter = new JobsOwnerIdentityAdapter(membership, options);

        adapter.MembershipLostToken.IsCancellationRequested.Should().BeFalse();

        // R9: on membership loss the token fires and the stamp gate closes.
        cts.Cancel();
        membership.Identity = null;

        adapter.MembershipLostToken.IsCancellationRequested.Should().BeTrue();
        adapter.TryGetStampOwner(out _).Should().BeFalse();
    }

    [Fact]
    public void default_owner_identity_uses_node_identifier_and_never_signals_loss()
    {
        var options = new SchedulerOptionsBuilder { NodeId = "machine-x" };
        var adapter = new DefaultJobsOwnerIdentity(options);

        adapter.DisplayOwner.Should().Be("machine-x");
        adapter.TryGetStampOwner(out var owner).Should().BeTrue();
        owner.Should().Be("machine-x");
        adapter.MembershipLostToken.Should().Be(CancellationToken.None);
    }

    private sealed class FakeNodeMembership : INodeMembership
    {
        public NodeIdentity? Identity { get; set; }

        public CancellationToken LostToken { get; set; } = CancellationToken.None;

        public CancellationToken LocalMembershipLostToken => LostToken;

        public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Identity.GetValueOrDefault());
        }

        public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([]);
        }

        public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult<IReadOnlyList<NodeLivenessSnapshot>>([]);
        }

        public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask.ConfigureAwait(false);

            yield break;
        }
    }
}
