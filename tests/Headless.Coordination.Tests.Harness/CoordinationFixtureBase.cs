// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public interface ICoordinationFixture
{
    void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup);
}

public static class CoordinationFixtureExtensions
{
    // All membership thresholds and the conformance tests' wall-clock sleeps are derived from this single scale.
    // Raising it widens every absolute timing margin uniformly (relative ordering is preserved) so the prune-window
    // assertions don't sit a few milliseconds from a boundary and flake under CI scheduling jitter.
    public const int TimeScale = 4;

    public static TimeSpan HeartbeatInterval => TimeSpan.FromMilliseconds(50 * TimeScale);

    public static TimeSpan SuspicionThreshold => TimeSpan.FromMilliseconds(150 * TimeScale);

    public static TimeSpan DeadThreshold => TimeSpan.FromMilliseconds(300 * TimeScale);

    public static TimeSpan DeadRetentionWindow => TimeSpan.FromMilliseconds(300 * TimeScale);

    // Time at which a dead node's retained state is fully pruned (DeadThreshold + DeadRetentionWindow).
    public static TimeSpan PruneThreshold => DeadThreshold + DeadRetentionWindow;

    // Comfortably inside the dead-but-retained window (between DeadThreshold and PruneThreshold) for "node is dead
    // but its retained state still exists" assertions.
    public static TimeSpan DeadButRetainedWait => (DeadThreshold + PruneThreshold) / 2;

    // Comfortably past the prune threshold for "retained state has been fully pruned" assertions.
    public static TimeSpan AfterPruneWait => PruneThreshold + (PruneThreshold / 2);

    public static async ValueTask<CoordinationNodeHandle> CreateNodeAsync(
        this ICoordinationFixture fixture,
        string clusterName,
        string nodeId,
        CancellationToken cancellationToken = default
    )
    {
        return await fixture
            .CreateNodeAsync(clusterName, nodeId, MembershipLostBehavior.StopMembershipOnly, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask<CoordinationNodeHandle> CreateNodeAsync(
        this ICoordinationFixture fixture,
        string clusterName,
        string nodeId,
        MembershipLostBehavior lostBehavior,
        CancellationToken cancellationToken = default
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var lifetime = new FakeHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        services.AddHeadlessCoordination(setup =>
        {
            fixture.ConfigureProvider(services, setup);
            setup.Configure(options =>
            {
                options.ClusterName = clusterName;
                options.ConfiguredNodeId = nodeId;
                options.HeartbeatInterval = HeartbeatInterval;
                options.SuspicionThreshold = SuspicionThreshold;
                options.DeadThreshold = DeadThreshold;
                options.DeadRetentionWindow = DeadRetentionWindow;
                options.MembershipLostBehavior = lostBehavior;
            });
        });

        var provider = services.BuildServiceProvider();

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(cancellationToken).ConfigureAwait(false);
        }

        return new CoordinationNodeHandle(provider, provider.GetRequiredService<INodeMembership>(), lifetime);
    }
}

public sealed class CoordinationNodeHandle(
    ServiceProvider services,
    INodeMembership membership,
    FakeHostApplicationLifetime lifetime
) : IAsyncDisposable
{
    public INodeMembership Membership { get; } = membership;

    public IServiceProvider Services { get; } = services;

    public FakeHostApplicationLifetime Lifetime { get; } = lifetime;

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync().ConfigureAwait(false);
    }
}
