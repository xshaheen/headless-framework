// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Tracing;

namespace Framework.Messages.Diagnostics;

[EventSource(Name = MessageDiagnosticListenerNames.MetricListenerName)]
public class MessageEventCounterSource : EventSource
{
    public static readonly MessageEventCounterSource Log = new();

    private IncrementingEventCounter? _publishPerSecondCounter;
    private IncrementingEventCounter? _consumePerSecondCounter;
    private IncrementingEventCounter? _subscriberInvokePerSecondCounter;

    private EventCounter? _invokeCounter;

    private MessageEventCounterSource() { }

    protected override void OnEventCommand(EventCommandEventArgs args)
    {
        if (args.Command == EventCommand.Enable)
        {
            _publishPerSecondCounter ??= new IncrementingEventCounter(
                MessageDiagnosticListenerNames.PublishedPerSec,
                this
            )
            {
                DisplayName = "Publish Rate",
                DisplayRateTimeScale = TimeSpan.FromSeconds(1),
            };

            _consumePerSecondCounter ??= new IncrementingEventCounter(
                MessageDiagnosticListenerNames.ConsumePerSec,
                this
            )
            {
                DisplayName = "Consume Rate",
                DisplayRateTimeScale = TimeSpan.FromSeconds(1),
            };

            _subscriberInvokePerSecondCounter ??= new IncrementingEventCounter(
                MessageDiagnosticListenerNames.InvokeSubscriberPerSec,
                this
            )
            {
                DisplayName = "Invoke Subscriber Rate",
                DisplayRateTimeScale = TimeSpan.FromSeconds(1),
            };

            _invokeCounter ??= new EventCounter(MessageDiagnosticListenerNames.InvokeSubscriberElapsedMs, this)
            {
                DisplayName = "Invoke Subscriber Elapsed Time",
                DisplayUnits = "ms",
            };
        }
    }

    public void WritePublishMetrics()
    {
        _publishPerSecondCounter?.Increment();
    }

    public void WriteConsumeMetrics()
    {
        _consumePerSecondCounter?.Increment();
    }

    public void WriteInvokeMetrics()
    {
        _subscriberInvokePerSecondCounter?.Increment();
    }

    public void WriteInvokeTimeMetrics(double elapsedMs)
    {
        _invokeCounter?.WriteMetric(elapsedMs);
    }

    protected override void Dispose(bool disposing)
    {
        _publishPerSecondCounter?.Dispose();
        _consumePerSecondCounter?.Dispose();
        _subscriberInvokePerSecondCounter?.Dispose();
        _invokeCounter?.Dispose();

        _publishPerSecondCounter = null;
        _consumePerSecondCounter = null;
        _subscriberInvokePerSecondCounter = null;
        _invokeCounter = null;

        base.Dispose(disposing);
    }
}
