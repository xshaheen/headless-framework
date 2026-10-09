// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("Kafka")]
public sealed class KafkaConsumerLagMetricTests(KafkaFixture fixture) : TestBase
{
    private const string _InstrumentName = "headless.messaging.kafka.consumer.lag";

    [Fact]
    public async Task should_report_committed_lag_from_broker_statistics()
    {
        // given: statistics every 200ms and a fast auto-commit, so the gauge follows commits within a second
        var group = $"group-{Guid.NewGuid():N}";
        await using var session = await fixture.CreateConformanceSessionAsync(
            AbortToken,
            group: group,
            createReplacement: false,
            configure: options =>
            {
                options.MainConfig["statistics.interval.ms"] = "200";
                options.MainConfig["auto.commit.interval.ms"] = "100";
            }
        );
        await session.StartAsync(cancellationToken: AbortToken);

        (await session.PublishAsync(_CreateMessage(session.Destination), AbortToken)).Succeeded.Should().BeTrue();
        (await session.PublishAsync(_CreateMessage(session.Destination), AbortToken)).Succeeded.Should().BeTrue();
        var first = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        var second = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when: only the first record settles, the committed offset stops one short of the end
        await session.Consumer.CommitAsync(first.SettlementValue, AbortToken);

        // then
        var lagAfterFirst = await _WaitForLagAsync(group, session.Destination, expected: 1);
        lagAfterFirst.Should().Be(1);

        await session.Consumer.CommitAsync(second.SettlementValue, AbortToken);
        var lagAfterSecond = await _WaitForLagAsync(group, session.Destination, expected: 0);
        lagAfterSecond.Should().Be(0);
    }

    private static async Task<long?> _WaitForLagAsync(string group, string topic, long expected)
    {
        long? last = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            last = _ReadLag(group, topic);
            if (last == expected)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
        }

        return last;
    }

    private static long? _ReadLag(string group, string topic)
    {
        long? lag = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal)
                    && string.Equals(instrument.Name, _InstrumentName, StringComparison.Ordinal)
                )
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>(
            (_, value, tags, _) =>
            {
                string? measuredGroup = null;
                string? measuredTopic = null;
                string? partition = null;
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case "messaging.consumer.group.name":
                            measuredGroup = tag.Value as string;
                            break;
                        case "messaging.destination.name":
                            measuredTopic = tag.Value as string;
                            break;
                        case "messaging.destination.partition.id":
                            partition = tag.Value as string;
                            break;
                    }
                }

                if (
                    string.Equals(measuredGroup, group, StringComparison.Ordinal)
                    && string.Equals(measuredTopic, topic, StringComparison.Ordinal)
                    && string.Equals(partition, "0", StringComparison.Ordinal)
                )
                {
                    lag = value;
                }
            }
        );

        listener.Start();
        listener.RecordObservableInstruments();

        return lag;
    }

    private static TransportMessage _CreateMessage(string destination)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
            [MessagingHeaders.MessageName] = destination,
            [MessagingHeaders.Intent] = nameof(MessageLane.Queue),
        };

        return new TransportMessage(headers, "lag"u8.ToArray());
    }
}
