// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using System.Text.Json;
using Confluent.Kafka;
using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class KafkaConsumerLagMetricsTests : TestBase
{
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    // Unique per test, so measurements from consumer clients other tests create in parallel are filtered out.
    private readonly string _groupId = "lag-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task should_report_lag_per_owned_partition_with_semconv_attributes()
    {
        // given
        await using var client = _CreateClient();
        client.PartitionsAssigned([new TopicPartition("orders", 0), new TopicPartition("orders", 1)]);

        // when
        client.OnStatistics(_Statistics(("orders", 0, 42), ("orders", 1, 0)));
        var measurements = _Collect();

        // then
        measurements.Should().HaveCount(2);
        var (lag, tags) = measurements.Single(m =>
            string.Equals((string?)m.Tags["messaging.destination.partition.id"], "0", StringComparison.Ordinal)
        );
        lag.Should().Be(42);
        tags.Should()
            .BeEquivalentTo(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["messaging.system"] = "kafka",
                    ["messaging.destination.name"] = "orders",
                    ["messaging.destination.partition.id"] = "0",
                    ["messaging.consumer.group.name"] = _groupId,
                }
            );
        measurements
            .Single(m =>
                string.Equals((string?)m.Tags["messaging.destination.partition.id"], "1", StringComparison.Ordinal)
            )
            .Value.Should()
            .Be(0);
    }

    [Fact]
    public async Task should_publish_gauge_with_expected_name_and_unit()
    {
        // given
        await using var client = _CreateClient();
        client.PartitionsAssigned([new TopicPartition("orders", 0)]);
        client.OnStatistics(_Statistics(("orders", 0, 7)));
        Instrument? found = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (string.Equals(instrument.Name, KafkaConsumerLagMetrics.InstrumentName, StringComparison.Ordinal))
                {
                    found = instrument;
                }
            },
        };

        // when
        listener.Start();

        // then
        found.Should().NotBeNull();
        found!.Meter.Name.Should().Be(MessagingDiagnostics.SourceName);
        found.Unit.Should().Be("{message}");
        found.Should().BeAssignableTo<ObservableGauge<long>>();
    }

    [Fact]
    public async Task should_skip_unknown_lag_and_internal_unassigned_partition()
    {
        // given
        await using var client = _CreateClient();
        client.PartitionsAssigned([new TopicPartition("orders", 0), new TopicPartition("orders", 1)]);

        // when
        client.OnStatistics(_Statistics(("orders", 0, -1), ("orders", 1, 5), ("orders", -1, 9)));
        var measurements = _Collect();

        // then
        measurements.Should().ContainSingle().Which.Value.Should().Be(5);
    }

    [Fact]
    public async Task should_skip_partitions_the_client_does_not_own()
    {
        // given
        await using var client = _CreateClient();
        client.PartitionsAssigned([new TopicPartition("orders", 0)]);

        // when
        client.OnStatistics(_Statistics(("orders", 0, 3), ("orders", 1, 8)));
        var measurements = _Collect();

        // then
        measurements.Should().ContainSingle().Which.Value.Should().Be(3);
    }

    [Fact]
    public async Task should_stop_reporting_partition_after_revoke()
    {
        // given
        await using var client = _CreateClient();
        client.PartitionsAssigned([new TopicPartition("orders", 0), new TopicPartition("orders", 1)]);
        client.OnStatistics(_Statistics(("orders", 0, 3), ("orders", 1, 8)));

        // when
        client.PartitionsRevoked([new TopicPartitionOffset("orders", 1, Offset.Unset)]);
        var measurements = _Collect();

        // then
        measurements.Should().ContainSingle().Which.Value.Should().Be(3);
    }

    [Fact]
    public async Task should_stop_reporting_partition_after_it_is_lost()
    {
        // given
        await using var client = _CreateClient();
        client.PartitionsAssigned([new TopicPartition("orders", 0)]);
        client.OnStatistics(_Statistics(("orders", 0, 3)));

        // when
        client.PartitionsLost([new TopicPartitionOffset("orders", 0, Offset.Unset)]);

        // then
        _Collect().Should().BeEmpty();
    }

    [Fact]
    public async Task should_stop_reporting_after_shutdown()
    {
        // given
        var client = _CreateClient();
        client.PartitionsAssigned([new TopicPartition("orders", 0)]);
        client.OnStatistics(_Statistics(("orders", 0, 3)));

        // when
        await client.DisposeAsync();
        client.OnStatistics(_Statistics(("orders", 0, 4)));

        // then
        _Collect().Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"topics":[1,2]}""")]
    [InlineData("""{"topics":{"orders":{"partitions":{"0":{"partition":"zero","consumer_lag":1}}}}}""")]
    public async Task should_log_and_keep_previous_lag_when_statistics_are_malformed(string statistics)
    {
        // given
        await using var client = _CreateClient();
        var logs = new List<LogMessageEventArgs>();
        client.OnLogCallback = logs.Add;
        client.PartitionsAssigned([new TopicPartition("orders", 0)]);
        client.OnStatistics(_Statistics(("orders", 0, 3)));

        // when
        var act = () => client.OnStatistics(statistics);

        // then
        act.Should().NotThrow();
        logs.Should().ContainSingle().Which.LogType.Should().Be(MqLogType.ConsumeError);
        _Collect().Should().ContainSingle().Which.Value.Should().Be(3);
    }

    [Fact]
    public void should_ignore_statistics_that_arrive_after_tracker_is_disposed()
    {
        // given: a statistics callback can race shutdown on the poll thread
        var tracker = new KafkaConsumerLagTracker(_groupId);
        tracker.Dispose();

        // when
        tracker.Update(_Statistics(("orders", 0, 3)), _ => true);

        // then
        tracker.Snapshot.Should().BeEmpty();
        _Collect().Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_nothing_when_statistics_have_no_topics()
    {
        // given
        await using var client = _CreateClient();
        client.PartitionsAssigned([new TopicPartition("orders", 0)]);
        client.OnStatistics(_Statistics(("orders", 0, 3)));

        // when
        client.OnStatistics("""{"name":"rdkafka#consumer-1","type":"consumer"}""");

        // then
        _Collect().Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_under_group_configured_in_main_config()
    {
        // given
        var options = new KafkaMessagingOptions { Servers = "localhost:9092" };
        options.MainConfig["group.id"] = _groupId;
        await using var client = new KafkaConsumerClient(
            "ignored-subscription",
            1,
            Options.Create(options),
            _serviceProvider
        );
        client.PartitionsAssigned([new TopicPartition("orders", 0)]);

        // when
        client.OnStatistics(_Statistics(("orders", 0, 3)));

        // then
        _Collect().Should().ContainSingle().Which.Value.Should().Be(3);
    }

    private KafkaConsumerClient _CreateClient()
    {
        var options = Options.Create(new KafkaMessagingOptions { Servers = "localhost:9092" });

        return new KafkaConsumerClient(_groupId, 1, options, _serviceProvider);
    }

    private List<(long Value, Dictionary<string, object?> Tags)> _Collect()
    {
        var measurements = new List<(long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal)
                    && string.Equals(instrument.Name, KafkaConsumerLagMetrics.InstrumentName, StringComparison.Ordinal)
                )
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>(
            (_, value, tags, _) =>
            {
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var tag in tags)
                {
                    dictionary[tag.Key] = tag.Value;
                }

                if (
                    dictionary.TryGetValue("messaging.consumer.group.name", out var group)
                    && string.Equals((string?)group, _groupId, StringComparison.Ordinal)
                )
                {
                    measurements.Add((value, dictionary));
                }
            }
        );

        listener.Start();
        listener.RecordObservableInstruments();

        return measurements;
    }

    // Mirrors the librdkafka STATISTICS.md layout: topics -> partitions keyed by id, with the -1 entry standing for the
    // internal unassigned partition, plus unrelated top-level and per-partition fields the parser must ignore.
    private static string _Statistics(params (string Topic, int Partition, long Lag)[] partitions)
    {
        var topics = partitions
            .GroupBy(p => p.Topic, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                    (object)
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["topic"] = g.Key,
                            ["age"] = 1000,
                            ["partitions"] = g.ToDictionary(
                                p => p.Partition.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                p =>
                                    (object)
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            ["partition"] = p.Partition,
                                            ["broker"] = 1,
                                            ["fetch_state"] = "active",
                                            ["hi_offset"] = 100,
                                            ["committed_offset"] = 100 - p.Lag,
                                            ["consumer_lag"] = p.Lag,
                                            ["consumer_lag_stored"] = p.Lag,
                                        },
                                StringComparer.Ordinal
                            ),
                        },
                StringComparer.Ordinal
            );

        return JsonSerializer.Serialize(
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = "rdkafka#consumer-1",
                ["type"] = "consumer",
                ["brokers"] = new Dictionary<string, object>(StringComparer.Ordinal),
                ["topics"] = topics,
                ["cgrp"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["state"] = "up" },
            }
        );
    }
}
