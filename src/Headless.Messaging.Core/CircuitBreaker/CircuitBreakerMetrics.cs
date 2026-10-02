// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// OpenTelemetry-compatible metrics for the circuit breaker, emitted via the shared
/// <c>Headless.Messaging</c> meter so existing OTel subscriptions pick them up automatically.
/// </summary>
internal sealed class CircuitBreakerMetrics
{
    /// <summary>
    /// Tag value used for unrecognized (not pre-registered) consumer names to prevent
    /// unbounded OTel cardinality from attacker-controlled input.
    /// </summary>
    internal const string UnknownConsumerTag = "_unknown";

    private const string _ConsumerTagKey = "messaging.consumer.group.name";

    private readonly Counter<long> _circuitTrips;
    private readonly Histogram<double> _openDuration;

    private Func<IReadOnlyDictionary<string, CircuitBreakerState>>? _stateSnapshot;
    private IReadOnlyDictionary<string, string> _safeTagCache = ImmutableDictionary<string, string>.Empty;

    public CircuitBreakerMetrics(IMeterFactory meterFactory)
    {
#pragma warning disable CA2000 // The IMeterFactory owns and disposes the meters it creates.
        var meter = meterFactory.Create("Headless.Messaging");
#pragma warning restore CA2000

        _circuitTrips = meter.CreateCounter<long>(
            "messaging.circuit_breaker.trips",
            description: "Number of times a consumer circuit breaker transitioned to Open"
        );

        _openDuration = meter.CreateHistogram<double>(
            "messaging.circuit_breaker.open_duration",
            unit: "s",
            description: "Duration in seconds that a consumer circuit was in Open state"
        );

        meter.CreateObservableGauge(
            "messaging.circuit_breaker.state",
            observeValues: _ObserveCircuitStates,
            description: "Current circuit state per consumer (0=Closed, 1=Open, 2=HalfOpen)"
        );
    }

    /// <summary>
    /// Registers the callback used by the observable gauge to pull current circuit states.
    /// </summary>
    public void RegisterStateCallback(Func<IReadOnlyDictionary<string, CircuitBreakerState>> callback)
    {
        Volatile.Write(ref _stateSnapshot, callback);
    }

    /// <summary>
    /// Sets the known consumer names for cardinality guards. When set, unrecognized consumer names
    /// are reported with the <see cref="UnknownConsumerTag"/> tag value instead of the real name.
    /// </summary>
    public void SetKnownConsumers(IReadOnlySet<string> knownConsumers)
    {
        var cache = new Dictionary<string, string>(knownConsumers.Count, StringComparer.Ordinal);

        foreach (var consumer in knownConsumers)
        {
            cache[consumer] = consumer;
        }

        Volatile.Write(ref _safeTagCache, cache);
    }

    /// <summary>Records a circuit trip (Closed → Open or HalfOpen → Open).</summary>
    public void RecordTrip(string consumerKey)
    {
        _circuitTrips.Add(1, new TagList { { _ConsumerTagKey, _SafeTag(consumerKey) } });
    }

    /// <summary>Records how long the circuit was open before transitioning to HalfOpen or Closed.</summary>
    public void RecordOpenDuration(string consumerKey, TimeSpan duration)
    {
        _openDuration.Record(duration.TotalSeconds, new TagList { { _ConsumerTagKey, _SafeTag(consumerKey) } });
    }

    private string _SafeTag(string consumerKey)
    {
        var cache = Volatile.Read(ref _safeTagCache);
        if (cache.Count == 0)
        {
            return consumerKey;
        }

        return cache.TryGetValue(consumerKey, out var safe) ? safe : UnknownConsumerTag;
    }

    private IEnumerable<Measurement<int>> _ObserveCircuitStates()
    {
        var snapshot = Volatile.Read(ref _stateSnapshot)?.Invoke();

        if (snapshot is null)
        {
            yield break;
        }

        foreach (var (consumer, state) in snapshot)
        {
            yield return new Measurement<int>((int)state, new TagList { { _ConsumerTagKey, _SafeTag(consumer) } });
        }
    }
}
