// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Registration;

/// <summary>
/// Options that cannot run safely stop the real host at startup, on the simulated broker: the validation error names
/// the setting, and no consumer subscribes or receives before it.
/// </summary>
public sealed class StartupValidationTests : TestBase
{
    [Fact]
    [Guarantee("MSG-REG-01")]
    public async Task should_fail_startup_before_any_consumer_subscribes_when_the_dispatch_timeout_is_not_above_the_inline_burst()
    {
        // given: three publish attempts of up to two minutes each, which a five-minute dispatch lease cannot cover
        var broker = new SimulatedBroker(new SimulationClock());
        var probe = new PipelineProbe();

        // when
        var start = () =>
            PipelineHosts.StartAsync(
                probe,
                AbortToken,
                static setup =>
                {
                    setup.Options.TransportPublishTimeout = TimeSpan.FromMinutes(2);
                    setup.Options.RetryPolicy.DispatchTimeout = TimeSpan.FromMinutes(5);
                    setup.Options.RetryPolicy.RetryStrategy.MaxRetryAttempts = 2;
                },
                broker
            );

        // then
        (await start.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should()
            .Contain("DispatchTimeout");
        broker.SubscriptionsFor(MessageLane.Queue, PipelineOrder.Name).Should().BeEmpty();
        broker
            .Events.Should()
            .NotContain(e => e.Kind == BrokerEventKind.Delivered || e.Kind == BrokerEventKind.ReceiveFailed);
    }
}
