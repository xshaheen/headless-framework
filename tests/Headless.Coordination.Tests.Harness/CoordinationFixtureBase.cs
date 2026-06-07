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
        var services = new ServiceCollection();
        services.AddLogging();

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
                options.MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly;
            });
        });

        var provider = services.BuildServiceProvider();

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(cancellationToken).ConfigureAwait(false);
        }

        return new CoordinationNodeHandle(provider, provider.GetRequiredService<INodeMembership>());
    }
}

public sealed class CoordinationNodeHandle(ServiceProvider services, INodeMembership membership) : IAsyncDisposable
{
    public INodeMembership Membership { get; } = membership;

    public IServiceProvider Services { get; } = services;

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync().ConfigureAwait(false);
    }
}
