// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Tracing;

namespace Headless.Messaging.Dashboard;

internal sealed class MessagingMetricsEventListener : EventListener
{
    public const int HistorySize = 300;
    private readonly TimeProvider _timeProvider;

    public MessagingMetricsEventListener(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;

        // The seconds before the listener started were never measured, so they read as no value, not as zero: a
        // chart of a node that just started must not show five minutes of idle throughput it never saw.
        for (var i = 0; i < HistorySize; i++)
        {
            PublishedPerSec.Add(null);
            InvokeSubscriberPerSec.Add(null);
            InvokeSubscriberElapsedMs.Add(null);
        }
    }

    public CircularBuffer<int?> PublishedPerSec { get; } = new(HistorySize);
    public CircularBuffer<int?> InvokeSubscriberPerSec { get; } = new(HistorySize);
    public CircularBuffer<int?> InvokeSubscriberElapsedMs { get; } = new(HistorySize);

    /// <summary>Returns the consumed-per-second values of the last <paramref name="seconds"/> seconds, oldest first.</summary>
    public IReadOnlyList<int?> LastSeconds(int seconds)
    {
        var values = InvokeSubscriberPerSec.ToArray();

        return values[Math.Max(0, values.Length - seconds)..];
    }

    /// <summary>
    /// Returns the last <see cref="HistorySize"/> seconds as Unix seconds and the value of each series in each,
    /// <see langword="null"/> for a second the listener did not measure.
    /// </summary>
    public (long[] Seconds, int?[] Published, int?[] Consumed, int?[] ConsumeElapsedMs) GetSeries()
    {
        var end = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var seconds = new long[HistorySize];

        for (var i = 0; i < HistorySize; i++)
        {
            seconds[i] = end - HistorySize + i;
        }

        return (
            seconds,
            PublishedPerSec.ToArray(),
            InvokeSubscriberPerSec.ToArray(),
            InvokeSubscriberElapsedMs.ToArray()
        );
    }

    public CircularBuffer<int?>[] GetRealTimeMetrics()
    {
        var warpArr = new CircularBuffer<int?>[4];

        var startTime = (int)_timeProvider.GetUtcNow().AddSeconds(-300).ToUnixTimeSeconds();
        var endTime = startTime + 300;

        var timeSerials = new CircularBuffer<int?>(HistorySize);
        for (var j = startTime; j < endTime; j++)
        {
            timeSerials.Add(j);
        }

        warpArr[0] = timeSerials;
        warpArr[1] = PublishedPerSec;
        warpArr[2] = InvokeSubscriberPerSec;
        warpArr[3] = InvokeSubscriberElapsedMs;

        return warpArr;
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (!source.Name.Equals(MessageDiagnosticListenerNames.MetricListenerName, StringComparison.Ordinal))
        {
            return;
        }

        EnableEvents(
            source,
            EventLevel.LogAlways,
            EventKeywords.All,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                //report interval
                ["EventCounterIntervalSec"] = "1",
            }
        );
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (!eventData.EventName!.Equals("EventCounters", StringComparison.Ordinal))
        {
            return;
        }

        var payload = (IDictionary<string, object>)eventData.Payload![0]!;

        var val = payload.Values.ToArray();

        if (string.Equals((string)val[0], MessageDiagnosticListenerNames.PublishedPerSec, StringComparison.Ordinal))
        {
            PublishedPerSec.Add(Convert.ToInt32(val[3], CultureInfo.InvariantCulture));
        }
        else if (
            string.Equals(
                (string)val[0],
                MessageDiagnosticListenerNames.InvokeSubscriberPerSec,
                StringComparison.Ordinal
            )
        )
        {
            InvokeSubscriberPerSec.Add(Convert.ToInt32(val[3], CultureInfo.InvariantCulture));
        }
        else if (
            string.Equals(
                (string)val[0],
                MessageDiagnosticListenerNames.InvokeSubscriberElapsedMs,
                StringComparison.Ordinal
            )
        )
        {
            var v = Convert.ToInt32(val[2], CultureInfo.InvariantCulture);
            InvokeSubscriberElapsedMs.Add(v == 0 ? null : v);
        }
    }
}
