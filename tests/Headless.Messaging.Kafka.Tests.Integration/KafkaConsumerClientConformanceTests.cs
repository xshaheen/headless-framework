// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging;
using Headless.Messaging.Configuration;

namespace Tests;

[Collection("Kafka")]
public sealed class KafkaConsumerClientConformanceTests(KafkaFixture fixture) : TransportConsumerConformanceTestsBase
{
    protected override string ProviderName => "Kafka";

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseKafka(fixture.ConnectionString);
    }

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
    public override Task should_match_production_runtime_capabilities()
    {
        return base.should_match_production_runtime_capabilities();
    }

    [Fact]
    public override Task should_commit_real_delivery_and_prevent_redelivery()
    {
        return base.should_commit_real_delivery_and_prevent_redelivery();
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

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas()
    {
        return TransportProviderConformance.AssertQueueOwnershipAsync(
            new KafkaProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public async Task should_terminally_commit_missing_required_headers_across_consumer_restart()
    {
        var destination = $"malformed-{Guid.NewGuid():N}";
        var group = $"group-{Guid.NewGuid():N}";
        var terminalLogs = 0;
        await using var session = await fixture.CreateConformanceSessionAsync(AbortToken, destination, group);
        await session.StartAsync(
            onLog: log =>
            {
                if (log.Reason?.Contains("terminally committed", StringComparison.Ordinal) == true)
                {
                    Interlocked.Increment(ref terminalLogs);
                }
            },
            cancellationToken: AbortToken
        );
        using var producer = new ProducerBuilder<string, byte[]>(
            new ProducerConfig { BootstrapServers = fixture.ConnectionString }
        ).Build();

        await producer.ProduceAsync(
            destination,
            new Message<string, byte[]>
            {
                Key = "poison",
                Value = "valid-body"u8.ToArray(),
                Headers = [],
            },
            AbortToken
        );

        using (var timeout = TimeSpan.FromSeconds(10).ToCancellationTokenSource(AbortToken))
        {
            while (Volatile.Read(ref terminalLogs) == 0)
            {
                await Task.Delay(20, timeout.Token);
            }
        }

        await session.StopAsync(TimeSpan.FromSeconds(2));
        await using var replacement = await session.CreateReplacementAsync(AbortToken);
        await replacement.StartAsync(cancellationToken: AbortToken);

        (await replacement.RemainsEmptyAsync(TimeSpan.FromSeconds(5), AbortToken)).Should().BeTrue();
        Volatile.Read(ref terminalLogs).Should().Be(1);
    }
}
