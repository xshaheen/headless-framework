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
    /// Tag value used for unrecognized (not pre-registered) group names to prevent
    /// unbounded OTel cardinality from attacker-controlled input.
    /// </summary>
    internal const string UnknownGroupTag = "_unknown";

    private readonly Counter<long> _circuitTrips;
    private readonly Histogram<double> _openDuration;

    private Func<IReadOnlyList<KeyValuePair<string, CircuitBreakerState>>>? _stateSnapshot;
    private IReadOnlySet<string>? _knownGroups;

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
    public void RegisterStateCallback(Func<IReadOnlyList<KeyValuePair<string, CircuitBreakerState>>> callback)
    {
        _stateSnapshot = callback;
    }

    /// <summary>
    /// Sets the known group names for cardinality guards. When set, unrecognized group names
    /// are reported with the <see cref="UnknownGroupTag"/> tag value instead of the real name.
    /// </summary>
    public void SetKnownGroups(IReadOnlySet<string> knownGroups)
    {
        _knownGroups = knownGroups;
    }

    /// <summary>Records a circuit trip (Closed → Open or HalfOpen → Open).</summary>
    public void RecordTrip(string groupName)
    {
        var tags = new TagList { { "messaging.consumer.group", _SafeTag(groupName) } };
        _circuitTrips.Add(1, tags);
    }

    /// <summary>Records how long the circuit was open before transitioning to HalfOpen or Closed.</summary>
    public void RecordOpenDuration(string groupName, TimeSpan duration)
    {
        var tags = new TagList { { "messaging.consumer.group", _SafeTag(groupName) } };
        _openDuration.Record(duration.TotalSeconds, tags);
    }

    private string _SafeTag(string groupName)
    {
        var known = _knownGroups;
        if (known is null) return UnknownGroupTag;
        return known.Contains(groupName) ? groupName : UnknownGroupTag;
    }

    private IEnumerable<Measurement<int>> _ObserveCircuitStates()
    {
        var snapshot = _stateSnapshot?.Invoke();

        if (snapshot is null)
        {
            yield break;
        }

        foreach (var (group, state) in snapshot)
        {
            yield return new Measurement<int>(
                (int)state,
                new KeyValuePair<string, object?>("messaging.consumer.group", _SafeTag(group))
            );
        }
    }
}
