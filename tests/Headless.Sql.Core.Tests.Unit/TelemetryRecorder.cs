// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Tests;

/// <summary>
/// Records the spans and measurements one named source emits. Tests using it run in a collection with parallelization
/// disabled, because the source and meter are process-wide.
/// </summary>
internal sealed class TelemetryRecorder : IDisposable
{
    private readonly ActivityListener? _activityListener;
    private readonly MeterListener? _meterListener;
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public TelemetryRecorder(string sourceName, bool spans = true, bool metrics = true)
    {
        if (spans)
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, sourceName, StringComparison.Ordinal),
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
                    if (string.Equals(instrument.Meter.Name, sourceName, StringComparison.Ordinal))
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

    public IReadOnlyList<Activity> Activities => [.. _activities];

    public IReadOnlyList<Measurement> Measurements => [.. _measurements];

    public IReadOnlyList<Measurement> Of(string instrument)
    {
        return [.. _measurements.Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal))];
    }

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

    internal sealed record Measurement(
        string Instrument,
        string? Unit,
        double Value,
        IReadOnlyDictionary<string, object?> Tags
    );
}
