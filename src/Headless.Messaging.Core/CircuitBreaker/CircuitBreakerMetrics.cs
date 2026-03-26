// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
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
    /// Tag value used for unrecognized (not pre-registered) group names to prevent
    /// unbounded OTel cardinality from attacker-controlled input.
    /// </summary>
    internal const string UnknownGroupTag = "_unknown";

    private const string GroupTagKey = "messaging.consumer.group";

    private readonly Counter<long> _circuitTrips;
    private readonly Histogram<double> _openDuration;

    private Func<IReadOnlyDictionary<string, CircuitBreakerState>>? _stateSnapshot;
    private IReadOnlyDictionary<string, string> _safeTagCache = ImmutableDictionary<string, string>.Empty;

    public CircuitBreakerMetrics(IMeterFactory meterFactory)
    {
        // Meter lifetime is managed by the IMeterFactory (DI container), not by this class.
#pragma warning disable CA2000
        var meter = meterFactory.Create("Headless.Messaging");
#pragma warning restore CA2000

        _circuitTrips = meter.CreateCounter<long>(
            "messaging.circuit_breaker.trips",
            description: "Number of times a consumer group circuit breaker transitioned to Open"
        );

        _openDuration = meter.CreateHistogram<double>(
            "messaging.circuit_breaker.open_duration",
            unit: "s",
            description: "Duration in seconds that a consumer group circuit was in Open state"
        );

        meter.CreateObservableGauge(
            "messaging.circuit_breaker.state",
            observeValues: _ObserveCircuitStates,
            description: "Current circuit state per group (0=Closed, 1=Open, 2=HalfOpen)"
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
    /// Sets the known group names for cardinality guards. When set, unrecognized group names
    /// are reported with the <see cref="UnknownGroupTag"/> tag value instead of the real name.
    /// </summary>
    public void SetKnownGroups(IReadOnlySet<string> knownGroups)
    {
        var cache = new Dictionary<string, string>(knownGroups.Count, StringComparer.Ordinal);

        foreach (var group in knownGroups)
        {
            cache[group] = group;
        }

        Volatile.Write(ref _safeTagCache, cache);
    }

    /// <summary>Records a circuit trip (Closed → Open or HalfOpen → Open).</summary>
    public void RecordTrip(string groupName)
    {
        _circuitTrips.Add(1, new TagList { { GroupTagKey, _SafeTag(groupName) } });
    }

    /// <summary>Records how long the circuit was open before transitioning to HalfOpen or Closed.</summary>
    public void RecordOpenDuration(string groupName, TimeSpan duration)
    {
        _openDuration.Record(duration.TotalSeconds, new TagList { { GroupTagKey, _SafeTag(groupName) } });
    }

    private string _SafeTag(string groupName)
    {
        var cache = Volatile.Read(ref _safeTagCache);
        if (cache.Count == 0)
        {
            return groupName;
        }

        return cache.TryGetValue(groupName, out var safe) ? safe : UnknownGroupTag;
    }

    private IEnumerable<Measurement<int>> _ObserveCircuitStates()
    {
        var snapshot = Volatile.Read(ref _stateSnapshot)?.Invoke();

        if (snapshot is null)
        {
            yield break;
        }

        foreach (var (group, state) in snapshot)
        {
            yield return new Measurement<int>((int)state, new TagList { { GroupTagKey, _SafeTag(group) } });
        }
    }
}
