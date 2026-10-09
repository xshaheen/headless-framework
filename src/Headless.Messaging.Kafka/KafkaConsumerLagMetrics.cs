// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Confluent.Kafka;
using Headless.Checks;

namespace Headless.Messaging.Kafka;

/// <summary>
/// Reports Kafka consumer lag per partition as an observable gauge on the shared messaging meter. Each consumer client
/// owns a <see cref="KafkaConsumerLagTracker"/> fed by librdkafka's statistics callback; the gauge reads the latest
/// value every tracker holds when a listener collects.
/// </summary>
internal static class KafkaConsumerLagMetrics
{
    // OpenTelemetry messaging semantic conventions define no consumer-lag instrument, and messaging.* is reserved for
    // the names they do define, so the instrument carries the framework prefix while its attributes use semconv names.
    internal const string InstrumentName = "headless.messaging.kafka.consumer.lag";
    internal const string Unit = "{message}";
    internal const string TagSystem = "messaging.system";
    internal const string TagDestination = "messaging.destination.name";
    internal const string TagPartition = "messaging.destination.partition.id";
    internal const string TagConsumerGroup = "messaging.consumer.group.name";
    internal const string SystemName = "kafka";

    private static readonly ConcurrentDictionary<KafkaConsumerLagTracker, byte> _Trackers = new();

    // Created once for the process, on first use: the shared meter lives as long as the process, and a gauge per
    // consumer client would leave an instrument behind for every client ever disposed.
    static KafkaConsumerLagMetrics()
    {
        MessagingDiagnostics.Meter.CreateObservableGauge(
            InstrumentName,
            _Observe,
            unit: Unit,
            description: "Messages between the committed offset and the end of the partition, per assigned partition, "
                + "as last reported by librdkafka statistics"
        );
    }

    internal static void Register(KafkaConsumerLagTracker tracker)
    {
        _Trackers.TryAdd(tracker, 0);
    }

    internal static void Unregister(KafkaConsumerLagTracker tracker)
    {
        _Trackers.TryRemove(tracker, out _);
    }

    private static IEnumerable<Measurement<long>> _Observe()
    {
        foreach (var tracker in _Trackers.Keys)
        {
            foreach (var entry in tracker.Snapshot.Values)
            {
                yield return new Measurement<long>(entry.Lag, entry.Tags);
            }
        }
    }
}

/// <summary>
/// Holds the latest consumer lag of the partitions one consumer client owns, parsed from librdkafka statistics JSON.
/// Partitions the client no longer owns are dropped, so a rebalance never leaves a stale value behind.
/// </summary>
internal sealed class KafkaConsumerLagTracker : IDisposable
{
    private static readonly Dictionary<TopicPartition, LagEntry> _Empty = [];

    private readonly string _groupId;
    private readonly Lock _lock = new();

    // Replaced as a whole on every change, so the gauge reads a consistent snapshot without taking the lock.
    private volatile Dictionary<TopicPartition, LagEntry> _snapshot = _Empty;
    private bool _disposed;

    public KafkaConsumerLagTracker(string groupId)
    {
        _groupId = Argument.IsNotNullOrEmpty(groupId);
        KafkaConsumerLagMetrics.Register(this);
    }

    internal IReadOnlyDictionary<TopicPartition, LagEntry> Snapshot => _snapshot;

    /// <summary>
    /// Replaces the recorded lag with the values in <paramref name="statisticsJson"/> for the partitions
    /// <paramref name="isOwned"/> accepts. A partition whose lag librdkafka does not know yet (-1) and the internal
    /// unassigned partition (-1) are not reported.
    /// </summary>
    /// <exception cref="JsonException">The payload is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">The payload does not have the librdkafka statistics shape.</exception>
    public void Update(string statisticsJson, Func<TopicPartition, bool> isOwned)
    {
        using var document = JsonDocument.Parse(statisticsJson);

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            var previous = _snapshot;
            var next = new Dictionary<TopicPartition, LagEntry>();

            // A consumer with no subscription yet has no topics object; that simply means nothing to report.
            if (document.RootElement.TryGetProperty("topics", out var topics))
            {
                foreach (var topic in topics.EnumerateObject())
                {
                    if (!topic.Value.TryGetProperty("partitions", out var partitions))
                    {
                        continue;
                    }

                    foreach (var partition in partitions.EnumerateObject())
                    {
                        var partitionId = partition.Value.GetProperty("partition").GetInt32();
                        if (partitionId < 0)
                        {
                            continue;
                        }

                        if (!partition.Value.TryGetProperty("consumer_lag", out var lagElement))
                        {
                            continue;
                        }

                        var lag = lagElement.GetInt64();
                        if (lag < 0)
                        {
                            continue;
                        }

                        var key = new TopicPartition(topic.Name, new Partition(partitionId));
                        if (!isOwned(key))
                        {
                            continue;
                        }

                        var tags = previous.TryGetValue(key, out var existing)
                            ? existing.Tags
                            : _BuildTags(topic.Name, partitionId);

                        next[key] = new LagEntry(lag, tags);
                    }
                }
            }

            _snapshot = next;
        }
    }

    /// <summary>Stops reporting <paramref name="partition"/>, after a revoke or a lost assignment.</summary>
    public void Remove(TopicPartition partition)
    {
        lock (_lock)
        {
            if (!_snapshot.ContainsKey(partition))
            {
                return;
            }

            var next = new Dictionary<TopicPartition, LagEntry>(_snapshot);
            next.Remove(partition);
            _snapshot = next;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _snapshot = _Empty;
        }

        KafkaConsumerLagMetrics.Unregister(this);
    }

    private KeyValuePair<string, object?>[] _BuildTags(string topic, int partition)
    {
        return
        [
            new(KafkaConsumerLagMetrics.TagSystem, KafkaConsumerLagMetrics.SystemName),
            new(KafkaConsumerLagMetrics.TagDestination, topic),
            new(KafkaConsumerLagMetrics.TagPartition, partition.ToString(CultureInfo.InvariantCulture)),
            new(KafkaConsumerLagMetrics.TagConsumerGroup, _groupId),
        ];
    }

    internal readonly record struct LagEntry(long Lag, KeyValuePair<string, object?>[] Tags);
}
