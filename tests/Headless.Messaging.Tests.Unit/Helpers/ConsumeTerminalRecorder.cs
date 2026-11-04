// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Headless.Messaging;

namespace Tests.Helpers;

/// <summary>
/// Records <c>headless.messaging.consume.terminal</c> measurements. The meter is process-wide and test classes run in
/// parallel, so a test reads only the measurements of a consumer identity no other running test uses.
/// </summary>
internal sealed class ConsumeTerminalRecorder : IDisposable
{
    private readonly ConcurrentQueue<(string? Consumer, string? Lane, string? ErrorType)> _measurements = new();
    private readonly MeterListener _listener;

    public ConsumeTerminalRecorder()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Name, MessagingMetrics.ConsumeTerminalName, StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) => _Record(tags));
        _listener.Start();
    }

    /// <summary>The lane and <c>error.type</c> of every give-up recorded for <paramref name="consumer"/>, in order.</summary>
    public IReadOnlyList<(string? Lane, string? ErrorType)> Of(string consumer) =>
        [
            .. _measurements
                .Where(m => string.Equals(m.Consumer, consumer, StringComparison.Ordinal))
                .Select(m => (m.Lane, m.ErrorType)),
        ];

    public void Dispose() => _listener.Dispose();

    private void _Record(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? consumer = null;
        string? lane = null;
        string? errorType = null;
        foreach (var tag in tags)
        {
            switch (tag.Key)
            {
                case MessagingMetrics.TagConsumerGroupName:
                    consumer = tag.Value as string;
                    break;
                case MessagingTags.Lane:
                    lane = tag.Value as string;
                    break;
                case MessagingMetrics.TagErrorType:
                    errorType = tag.Value as string;
                    break;
            }
        }

        _measurements.Enqueue((consumer, lane, errorType));
    }
}
