// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Headless.Coordination;
using Microsoft.Extensions.Hosting;

namespace Tests;

public interface ICoordinationFixture
{
    void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup);
}

public static class CoordinationFixtureExtensions
{
    private static TimeSpan HeartbeatInterval => TimeSpan.FromMilliseconds(50);

    private static TimeSpan SuspicionThreshold => TimeSpan.FromMilliseconds(150);

    private static TimeSpan DeadThreshold => TimeSpan.FromMilliseconds(300);

    private static TimeSpan DeadRetentionWindow => TimeSpan.FromMilliseconds(300);

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
