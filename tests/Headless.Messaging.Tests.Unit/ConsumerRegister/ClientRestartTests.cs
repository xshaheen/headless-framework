// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
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

    [Theory]
    [Guarantee("MSG-CONN-03")]
    [InlineData(MqLogType.ConsumerRegistered)]
    [InlineData(MqLogType.RedisConsumeError)]
    public async Task should_restart_a_client_whose_channel_shut_down_when_another_client_of_its_subscription_reports(
        MqLogType report
    )
    {
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

        // when: one client's channel shuts down, then the other reports a registration or a consume error
        clients[0].CloseChannel();
        clients[1]
            .OnLogCallback!(new LogMessageEventArgs { LogType = report, Reason = "Reported by the other client." });
        await host.RunForAsync(_PastTransportCheck, AbortToken);

        // then: the transport check rebuilt the subscription, which closes the client that kept running, and the rebuilt
        // clients receive. The close is the signal to wait on: a rebuilt client joins its broker group without a broker
        // event, so a wait on the open-client count can wake before the last one is listed and never wake again.
        await host.WaitUntilAsync(() => !host.Broker.ClientsOf(subscription).Contains(clients[1]), AbortToken);
        await host.EnqueueAsync("order-1", AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("order-1", 1), AbortToken);
    }
}
