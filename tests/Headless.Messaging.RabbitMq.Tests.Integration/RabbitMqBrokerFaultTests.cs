// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqBrokerFaultTests(RabbitMqFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "RabbitMQ";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause()
    {
        return base.should_resume_delivery_once_after_consumer_pause();
    }

    protected override TransportMessage CreateFaultProbe(string destination)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
            [MessagingHeaders.MessageName] = destination,
            ["x-headless-conformance"] = "consumer-pause-recovery",
        };

        return new TransportMessage(headers, "post-pause-probe"u8.ToArray());
    }
}
