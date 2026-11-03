// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;

namespace Tests;

[Collection("Kafka")]
public sealed class KafkaBrokerFaultTests(KafkaFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "Kafka";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    protected override async ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        // A broker of its own on a fixed host port: a restart must not disturb the collection's broker, and the
        // session must find the restarted broker at the address it already holds.
        var broker = new FixedPortKafkaFixture();

        try
        {
            await broker.StartAsync();
            var group = $"group-{Guid.NewGuid():N}";
            var session = await fixture.CreateConformanceSessionAsync(
                cancellationToken,
                group: group,
                createReplacement: false,
                configure: options =>
                {
                    options.Servers = broker.ConnectionString;
                    options.MainConfig["auto.commit.interval.ms"] = "100";
                }
            );

            return new InterruptibleTransportSession(
                session,
                async token =>
                {
                    // CommitAsync only stores the offset; auto-commit sends it to the broker later. Restarting first
                    // loses it, and the broker then correctly replays the settled delivery, so wait for it to land.
                    await _WaitForCommittedOffsetAsync(broker.ConnectionString, group, session.Destination, token);
                    await broker.Container.StopAsync(token);
                    await broker.Container.StartAsync(token);
                },
                () => ((IAsyncDisposable)broker).DisposeAsync()
            );
        }
        catch
        {
            await ((IAsyncDisposable)broker).DisposeAsync();
            throw;
        }
    }

    private static async Task _WaitForCommittedOffsetAsync(
        string servers,
        string group,
        string topic,
        CancellationToken cancellationToken
    )
    {
        using var reader = new ConsumerBuilder<Ignore, Ignore>(
            new ConsumerConfig { BootstrapServers = servers, GroupId = group }
        ).Build();
        var partition = new TopicPartition(topic, new Partition(0));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (reader.Committed([partition], TimeSpan.FromSeconds(5))[0].Offset.Value > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new TimeoutException($"Group {group} committed no offset on {topic} within 30 s.");
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause()
    {
        return base.should_resume_delivery_once_after_consumer_pause();
    }

    [Fact]
    public override Task should_resume_delivery_once_after_broker_restart()
    {
        return base.should_resume_delivery_once_after_broker_restart();
    }
}
