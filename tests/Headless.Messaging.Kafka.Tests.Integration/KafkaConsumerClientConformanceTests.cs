// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("Kafka")]
public sealed class KafkaConsumerClientConformanceTests(KafkaFixture fixture) : TransportConsumerConformanceTestsBase
{
    protected override string ProviderName => "Kafka";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers()
    {
        return base.should_round_trip_queue_message_body_and_headers();
    }

    [Fact]
    public override async Task should_commit_real_delivery_and_prevent_redelivery()
    {
        RequireSupport(TransportConformanceScenario.CommitSettlement);

        var destination = $"conf-{Guid.NewGuid():N}";
        var group = $"group-{Guid.NewGuid():N}";
        var committedId = Guid.NewGuid().ToString("N");

        await using (var first = await fixture.CreateConformanceSessionAsync(AbortToken, destination, group))
        {
            await first.StartAsync(cancellationToken: AbortToken);
            var result = await first.PublishAsync(_CreateMessage(destination, committedId), AbortToken);
            result.Succeeded.Should().BeTrue();

            var delivery = await first.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
            await first.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
        }

        await using var replacement = await fixture.CreateConformanceSessionAsync(AbortToken, destination, group);
        await replacement.StartAsync(cancellationToken: AbortToken);
        var oldRecordStayedCommitted = await replacement.RemainsEmptyAsync(TimeSpan.FromSeconds(3), AbortToken);
        oldRecordStayedCommitted.Should().BeTrue("the same consumer group must resume after the committed offset");

        var generationId = Guid.NewGuid().ToString("N");
        var generationResult = await replacement.PublishAsync(_CreateMessage(destination, generationId), AbortToken);
        generationResult.Succeeded.Should().BeTrue();
        var generationDelivery = await replacement.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        generationDelivery.Message.Id.Should().Be(generationId);
        await replacement.Consumer.CommitAsync(generationDelivery.SettlementValue, AbortToken);
    }

    [Fact]
    public override Task should_reject_real_delivery_and_observe_redelivery()
    {
        return base.should_reject_real_delivery_and_observe_redelivery();
    }

    [Fact]
    public override Task should_isolate_unique_destinations()
    {
        return base.should_isolate_unique_destinations();
    }

    [Fact]
    public override Task should_shutdown_idle_consumer_within_bound()
    {
        return base.should_shutdown_idle_consumer_within_bound();
    }

    [Fact]
    public override Task should_bound_shutdown_while_handler_is_active()
    {
        return base.should_bound_shutdown_while_handler_is_active();
    }

    private static TransportMessage _CreateMessage(string destination, string messageId)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = destination,
            ["x-headless-conformance"] = "same-group-restart",
        };

        return new TransportMessage(headers, "kafka-commit-probe"u8.ToArray());
    }
}
