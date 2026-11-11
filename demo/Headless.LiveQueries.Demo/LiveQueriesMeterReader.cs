// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Demo;

/// <summary>
/// Demo code: keeps running totals of the <c>Headless.LiveQueries</c> meter so the page and the benchmark script
/// can read them over HTTP. A real host exports the meter through OpenTelemetry instead.
/// </summary>
public sealed class LiveQueriesMeterReader : IDisposable
{
    private const string _MeterName = "Headless.LiveQueries";

    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, long> _totals = new(StringComparer.Ordinal);

    public LiveQueriesMeterReader()
    {
        _listener.InstrumentPublished = static (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, _MeterName, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => _Add(instrument.Name, value));
        _listener.Start();
    }

    /// <summary>The total of each instrument that has reported, by instrument name.</summary>
    public IReadOnlyDictionary<string, long> Read()
    {
        return new SortedDictionary<string, long>(_totals, StringComparer.Ordinal);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private void _Add(string instrument, long value)
    {
        _totals.AddOrUpdate(instrument, static (_, added) => added, static (_, total, added) => total + added, value);
    }
}
