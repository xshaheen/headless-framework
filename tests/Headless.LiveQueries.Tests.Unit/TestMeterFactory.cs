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
    private readonly ConcurrentDictionary<string, long> _totals = new(StringComparer.Ordinal);
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
            (instrument, measurement, _, _) =>
                _totals.AddOrUpdate(
                    instrument.Name,
                    static (_, added) => added,
                    static (_, total, added) => total + added,
                    measurement
                )
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
    public long Total(string instrument) => _totals.GetValueOrDefault(instrument);

    public void Dispose()
    {
        _listener.Dispose();

        foreach (var meter in _meters)
        {
            meter.Dispose();
        }
    }
}
