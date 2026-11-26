// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class CoordinationClusterSummaryTests
{
    [Fact]
    public void should_describe_default_options_with_the_extension_provider_name()
    {
        // given
        using var provider = _Build();

        // when
        var summary = provider.GetRequiredService<CoordinationClusterSummary>();

        // then
        summary.ProviderName.Should().Be("Custom");
        summary.ClusterName.Should().Be(CoordinationOptions.DefaultClusterName);
        summary.Role.Should().BeNull();
        summary.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(5));
        summary.SuspicionThreshold.Should().Be(TimeSpan.FromSeconds(15));
        summary.DeadThreshold.Should().Be(TimeSpan.FromSeconds(30));
        summary.DeadRetentionWindow.Should().Be(TimeSpan.FromSeconds(30));
        summary.SelfFenceMargin.Should().Be(TimeSpan.FromSeconds(5));
        summary.MembershipHeartbeatEnabled.Should().BeTrue();
    }

    [Fact]
    public void should_report_configured_options()
    {
        // given
        using var provider = _Build(configure: static options =>
        {
            options.ClusterName = "orders";
            options.Role = "worker";
            options.HeartbeatInterval = TimeSpan.FromSeconds(2);
            options.SuspicionThreshold = TimeSpan.FromSeconds(7);
            options.DeadThreshold = TimeSpan.FromSeconds(12);
            options.DeadRetentionWindow = TimeSpan.FromSeconds(40);
        });

        // when
        var summary = provider.GetRequiredService<CoordinationClusterSummary>();

        // then
        summary.ClusterName.Should().Be("orders");
        summary.Role.Should().Be("worker");
        summary.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(2));
        summary.SuspicionThreshold.Should().Be(TimeSpan.FromSeconds(7));
        summary.DeadThreshold.Should().Be(TimeSpan.FromSeconds(12));
        summary.DeadRetentionWindow.Should().Be(TimeSpan.FromSeconds(40));
        summary.SelfFenceMargin.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void should_resolve_the_default_margin_the_fence_uses_for_a_short_dead_threshold()
    {
        // given
        using var provider = _Build(configure: static options =>
        {
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(200);
            options.SuspicionThreshold = TimeSpan.FromMilliseconds(600);
            options.DeadThreshold = TimeSpan.FromMilliseconds(1200);
            options.DeadRetentionWindow = TimeSpan.FromMilliseconds(1200);
        });

        // when
        var summary = provider.GetRequiredService<CoordinationClusterSummary>();
        var membership = provider.GetRequiredService<MembershipService>();

        // then
        summary.SelfFenceMargin.Should().Be(TimeSpan.FromMilliseconds(200));
        (summary.DeadThreshold - summary.SelfFenceMargin).Should().Be(membership.FenceBudget);
    }

    [Fact]
    public void should_report_an_explicit_margin_unchanged()
    {
        // given
        using var provider = _Build(configure: static options => options.SelfFenceMargin = TimeSpan.FromSeconds(3));

        // when
        var summary = provider.GetRequiredService<CoordinationClusterSummary>();

        // then
        summary.SelfFenceMargin.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void should_report_a_disabled_heartbeat()
    {
        // given
        using var provider = _Build(disableHeartbeat: true);

        // when
        var summary = provider.GetRequiredService<CoordinationClusterSummary>();

        // then
        summary.MembershipHeartbeatEnabled.Should().BeFalse();
    }

    [Fact]
    public void should_build_the_summary_without_calling_the_store()
    {
        // given
        using var provider = _Build();

        // when
        _ = provider.GetRequiredService<CoordinationClusterSummary>();

        // then
        var store = provider.GetRequiredService<FakeMembershipStore>();
        store.AllocateIncarnationCalls.Should().Be(0);
        store.ReadLivenessCalls.Should().Be(0);
        store.ReadNodeLivenessCalls.Should().Be(0);
        store.ReadLiveNodesCalls.Should().Be(0);
        store.Heartbeats.Should().BeEmpty();
        store.Descriptors.Should().BeEmpty();
    }

    private static ServiceProvider _Build(Action<CoordinationOptions>? configure = null, bool disableHeartbeat = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCoordination(setup =>
        {
            setup.RegisterExtension(new CustomProviderExtension());

            if (configure is not null)
            {
                setup.Configure(configure);
            }

            if (disableHeartbeat)
            {
                setup.DisableMembershipHeartbeat();
            }
        });

        return services.BuildServiceProvider();
    }

    private sealed class CustomProviderExtension : ICoordinationProviderOptionsExtension
    {
        public string ProviderName => "Custom";

        public void AddServices(IServiceCollection services)
        {
            services.AddCoordinationCore<FakeMembershipStore>();
        }
    }
}
