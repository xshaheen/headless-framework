// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupMessagingCoordinationTests
{
    [Fact]
    public void should_register_null_node_membership_by_default()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        var membership = provider.GetRequiredService<INodeMembership>();
        membership.Should().BeOfType<NullNodeMembership>();
        membership.Identity.Should().BeNull();
    }

    [Fact]
    public void should_not_shadow_registered_node_membership()
    {
        // given
        var services = new ServiceCollection();
        var membership = new TestNodeMembership();
        services.AddSingleton<INodeMembership>(membership);

        // when
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<INodeMembership>().Should().BeSameAs(membership);
    }

    private sealed class TestNodeMembership : INodeMembership
    {
        public NodeIdentity? Identity { get; } = new(new NodeId("node-a"), new NodeIncarnation(7));

        public CancellationToken LocalMembershipLostToken => CancellationToken.None;

        public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Identity!.Value);
        }

        public ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(identity == Identity);
        }

        public ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([Identity!.Value]);
        }

        public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<NodeLivenessSnapshot>>([]);
        }

        public async IAsyncEnumerable<NodeMembershipEvent> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();

            yield break;
        }
    }
}
