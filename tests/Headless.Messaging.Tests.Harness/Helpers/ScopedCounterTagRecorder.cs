// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Headless.Messaging;

namespace Tests.Helpers;

/// <summary>
/// Records the values one tag takes on a messaging counter, counting only measurements made in the execution context
/// of the test that created the recorder and the work that test starts.
/// </summary>
/// <remarks>
/// Messaging counters are process-wide and carry no message tag, so an unscoped listener would also hear the tests
/// running in parallel and could see an outcome before its own test produced one.
/// </remarks>
[PublicAPI]
public sealed class ScopedCounterTagRecorder : IDisposable
{
    private static readonly AsyncLocal<object?> _Scope = new();
    private readonly MeterListener _listener = new();
    private readonly object _scope = new();

    public ScopedCounterTagRecorder(string instrumentName, string tagKey)
    {
        _Scope.Value = _scope;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (
                string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal)
                && string.Equals(instrument.Name, instrumentName, StringComparison.Ordinal)
            )
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                if (!ReferenceEquals(_Scope.Value, _scope))
                {
                    return;
                }

                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, tagKey, StringComparison.Ordinal) && tag.Value is string value)
                    {
                        Values.Enqueue(value);
                    }
                }
            }
        );
        _listener.Start();
    }

    public ConcurrentQueue<string> Values { get; } = new();

    public void Dispose()
    {
        _listener.Dispose();
    }
}
