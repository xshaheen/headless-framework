// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests;

/// <summary>
/// The consumer register's transport check, on the real pipeline against the simulated broker: a client whose channel
/// shut down is restarted at the next check, whatever the other clients of its subscription report meanwhile.
/// </summary>
public sealed class ClientRestartTests : TestBase
{
    // The transport check runs every 30 seconds; a little more than one interval covers the next check.
    private static readonly TimeSpan _PastTransportCheck = TimeSpan.FromSeconds(31);

    [Fact]
    [Guarantee("MSG-CONN-03")]
    public async Task should_restart_a_client_whose_channel_shut_down_when_another_client_of_its_subscription_registers()
    {
        MessagingGuarantees.RequireSupport("MSG-CONN-03");

        // given: two clients on one subscription
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup => setup.Options.ConsumerThreadCount = 2
        );
        var subscription = host
            .Broker.SubscriptionsFor(MessageLane.Queue, PipelineOrder.Name)
            .Should()
            .ContainSingle()
            .Subject;
        var clients = host.Broker.ClientsOf(subscription);
        clients.Should().HaveCount(2);

        // when: one client's channel shuts down, then the other reports its consumer registered
        clients[0].CloseChannel();
        clients[1].ReportRegistered();
        await host.RunForAsync(_PastTransportCheck, AbortToken);

        // then: the subscription is back to two open clients, so the dead one was restarted
        await host.WaitUntilAsync(() => host.Broker.ClientsOf(subscription).Count == 2, AbortToken);
        await host.EnqueueAsync("order-1", AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("order-1", 1), AbortToken);
    }
}
