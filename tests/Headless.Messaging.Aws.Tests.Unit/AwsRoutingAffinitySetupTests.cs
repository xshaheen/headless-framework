// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class AwsRoutingAffinitySetupTests : TestBase
{
    [Theory]
    [InlineData("orders", false)]
    [InlineData("orders.fifo", true)]
    public async Task should_snapshot_fifo_affinity_without_resolving_transports(string destination, bool supported)
    {
        var effects = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseAws(options => options.Region = Amazon.RegionEndpoint.USEast1);
            setup.Queue.ForMessage<AffinityContract>(message => message.Contract(destination).RequireRoutingAffinity());
        });
        services.AddSingleton<IQueueTransport>(_ =>
        {
            effects++;
            return Substitute.For<IQueueTransport>();
        });
        services.AddSingleton<IBusTransport>(_ =>
        {
            effects++;
            return Substitute.For<IBusTransport>();
        });
        await using var provider = services.BuildServiceProvider();

        var model = provider.GetRequiredService<IMessagingCapabilityModel>();
        model.Providers.Single().RoutingAffinityRoutes.Any().Should().Be(supported);
        if (!supported)
        {
            var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
            await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*affinity*unsupported*");
        }

        effects.Should().Be(0);
    }

    private sealed record AffinityContract;
}
