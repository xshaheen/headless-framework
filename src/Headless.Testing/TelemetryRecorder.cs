// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Checks;

namespace Headless.Testing;

/// <summary>
/// Records the stopped spans and meter measurements one named source emits, through BCL
/// <see cref="ActivityListener"/> and <see cref="MeterListener"/>, so a test can assert on telemetry without an
/// OpenTelemetry pipeline. Both queues are thread-safe: the listeners are process-global and their callbacks run
/// concurrently when tests running in parallel emit through the same source. The named source and meter are still
/// process-wide, so this recorder also receives other tests' telemetry for that name — filter assertions by a tag
/// unique to the test, or run the test class in a collection with parallelization disabled.
/// </summary>
[PublicAPI]
public sealed class TelemetryRecorder : IDisposable
{
    private readonly string _sourceName;
    private readonly ActivityListener? _activityListener;
    private readonly MeterListener? _meterListener;
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    /// <summary>
    /// Starts listening to the source (and same-named meter) <paramref name="sourceName" />. Only stopped activities
    /// are captured; use <paramref name="spans" /> and <paramref name="metrics" /> to subscribe to one signal only.
    /// </summary>
    /// <param name="sourceName">The activity source and meter name to record.</param>
    /// <param name="spans">Whether to record stopped activities.</param>
    /// <param name="metrics">Whether to record measurements.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sourceName" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="sourceName" /> is empty or white space.</exception>
    public TelemetryRecorder(string sourceName, bool spans = true, bool metrics = true)
    {
        _sourceName = Argument.IsNotNullOrWhiteSpace(sourceName);

        if (spans)
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, _sourceName, StringComparison.Ordinal),
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_activityListener);
        }

        if (metrics)
        {
            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (string.Equals(instrument.Meter.Name, _sourceName, StringComparison.Ordinal))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _meterListener.SetMeasurementEventCallback<int>((i, v, t, _) => _Record(i, v, t));
            _meterListener.SetMeasurementEventCallback<long>((i, v, t, _) => _Record(i, v, t));
            _meterListener.SetMeasurementEventCallback<double>((i, v, t, _) => _Record(i, v, t));
            _meterListener.Start();
        }
    }

    /// <summary>The activities this recorder has watched stop, oldest first. Safe to read while other threads record.</summary>
    public IReadOnlyList<Activity> Activities => [.. _activities];

    /// <summary>The measurements this recorder has captured, oldest first. Safe to read while other threads record.</summary>
    public IReadOnlyList<Measurement> Measurements => [.. _measurements];

    /// <summary>The captured measurements of <paramref name="instrument" /> only, oldest first.</summary>
    /// <param name="instrument">The instrument name to filter by.</param>
    /// <exception cref="ArgumentNullException"><paramref name="instrument" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="instrument" /> is empty or white space.</exception>
    public IReadOnlyList<Measurement> Of(string instrument)
    {
        Argument.IsNotNullOrWhiteSpace(instrument);

        return [.. _measurements.Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal))];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activityListener?.Dispose();
        _meterListener?.Dispose();
    }

    private void _Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        _measurements.Enqueue(new Measurement(instrument.Name, instrument.Unit, value, copy));
    }

    /// <summary>One captured measurement: the instrument that recorded it and the value with its tags.</summary>
    /// <param name="Instrument">The instrument name that recorded the measurement.</param>
    /// <param name="Unit">The instrument's unit of measurement, if it declared one.</param>
    /// <param name="Value">The recorded value.</param>
    /// <param name="Tags">The measurement's tags, keyed by tag name.</param>
    public sealed record Measurement(
        string Instrument,
        string? Unit,
        double Value,
        IReadOnlyDictionary<string, object?> Tags
    );
}
