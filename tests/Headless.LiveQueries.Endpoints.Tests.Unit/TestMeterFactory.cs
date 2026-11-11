// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Tests;

/// <summary>
/// A meter factory that sums the measurements of the meters it created and of no other, so a test never hears the
/// instruments of a test running beside it.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly ConcurrentBag<Meter> _meters = [];
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _totals = new(StringComparer.Ordinal);
    private readonly List<(string Instrument, long Total, TaskCompletionSource Reached)> _waiting = [];
    private readonly MeterListener _listener = new();

    public TestMeterFactory()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, this))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) => _Add(instrument.Name, measurement)
        );
        _listener.Start();
    }

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(
            new MeterOptions(options.Name)
            {
                Version = options.Version,
                Tags = options.Tags,
                Scope = this,
            }
        );
        _meters.Add(meter);

        return meter;
    }

    /// <summary>Returns the sum of everything recorded on the instrument named <paramref name="instrument" />.</summary>
    public long Total(string instrument)
    {
        lock (_lock)
        {
            return _totals.GetValueOrDefault(instrument);
        }
    }

    /// <summary>Completes once the sum recorded on <paramref name="instrument" /> is <paramref name="total" />.</summary>
    public Task ReachedAsync(string instrument, long total)
    {
        lock (_lock)
        {
            if (_totals.GetValueOrDefault(instrument) == total)
            {
                return Task.CompletedTask;
            }

            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting.Add((instrument, total, reached));

            return reached.Task;
        }
    }

    public void Dispose()
    {
        _listener.Dispose();

        foreach (var meter in _meters)
        {
            meter.Dispose();
        }
    }

    private void _Add(string instrument, long measurement)
    {
        lock (_lock)
        {
            var total = _totals.GetValueOrDefault(instrument) + measurement;
            _totals[instrument] = total;

            for (var index = _waiting.Count - 1; index >= 0; index--)
            {
                var waiting = _waiting[index];

                if (string.Equals(waiting.Instrument, instrument, StringComparison.Ordinal) && waiting.Total == total)
                {
                    _waiting.RemoveAt(index);
                    waiting.Reached.TrySetResult();
                }
            }
        }
    }
}
