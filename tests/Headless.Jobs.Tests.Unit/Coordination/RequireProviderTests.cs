// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Coordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Coordination;

public sealed class RequireProviderTests
{
    [Fact]
    public void durable_path_without_a_coordination_provider_fails_fast()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(o => o.UseEntityFramework());

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessCoordination*");
    }

    [Fact]
    public void durable_path_with_a_registered_provider_builds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INodeMembership>(new StubNodeMembership());

        var act = () => services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(o => o.UseEntityFramework());

        act.Should().NotThrow();
    }

    [Fact]
    public void durable_path_rejects_the_null_membership_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INodeMembership, NullNodeMembership>();

        var act = () => services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(o => o.UseEntityFramework());

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessCoordination*");
    }

    [Fact]
    public void durable_path_rejects_a_null_membership_provider_registered_as_an_instance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INodeMembership>(new NullNodeMembership());

        var act = () => services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(o => o.UseEntityFramework());

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessCoordination*");
    }

    [Fact]
    public void durable_path_accepts_a_real_provider_registered_after_a_null_fallback()
    {
        // Mirrors the last-wins contract: a consumer registers NullNodeMembership first, coordination replaces it.
        var services = new ServiceCollection();
        services.AddSingleton<INodeMembership, NullNodeMembership>();
        services.AddSingleton<INodeMembership>(new StubNodeMembership());

        var act = () => services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(o => o.UseEntityFramework());

        act.Should().NotThrow();
    }

    [Fact]
    public void in_memory_path_runs_without_a_coordination_provider()
    {
        var services = new ServiceCollection();

        services.AddHeadlessJobs();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IJobsOwnerIdentity>().Should().BeOfType<DefaultJobsOwnerIdentity>();
    }

    private sealed class StubNodeMembership : INodeMembership
    {
        public NodeIdentity? Identity => new(new NodeId("stub"), new NodeIncarnation(1));

        public CancellationToken LocalMembershipLostToken => CancellationToken.None;

        public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Identity!.Value);
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
