// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// OpenTelemetry-compatible metrics for the circuit breaker, emitted via the shared
/// <c>Headless.Messaging</c> meter so existing OTel subscriptions pick them up automatically.
/// </summary>
internal sealed class CircuitBreakerMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _circuitTrips;
    private readonly Histogram<double> _openDuration;

    public CircuitBreakerMetrics()
    {
        _meter = new Meter("Headless.Messaging", "1.0.0");

        _circuitTrips = _meter.CreateCounter<long>(
            "messaging.circuit_breaker.trips",
            description: "Number of times a consumer group circuit breaker transitioned to Open"
        );

        _openDuration = _meter.CreateHistogram<double>(
            "messaging.circuit_breaker.open_duration",
            unit: "s",
            description: "Duration in seconds that a consumer group circuit was in Open state"
        );
    }

    /// <summary>Records a circuit trip (Closed → Open or HalfOpen → Open).</summary>
    public void RecordTrip(string groupName)
    {
        var tags = new TagList { { "messaging.consumer.group", groupName } };
        _circuitTrips.Add(1, tags);
    }

    /// <summary>Records how long the circuit was open before transitioning to HalfOpen or Closed.</summary>
    public void RecordOpenDuration(string groupName, double durationMs)
    {
        var tags = new TagList { { "messaging.consumer.group", groupName } };
        _openDuration.Record(durationMs / 1000.0, tags);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
