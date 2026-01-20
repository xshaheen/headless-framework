// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

/// <summary>
/// Messaging instrumentation.
/// </summary>
internal sealed class MessagingInstrumentation : IDisposable
{
    private readonly MessagingDiagnosticSourceSubscriber? _diagnosticSourceSubscriber;
    private readonly MessagingMetrics? _metrics;

    public MessagingInstrumentation(DiagnosticListener diagnosticListener, MessagingMetrics? metrics = null)
    {
        _metrics = metrics;
        _diagnosticSourceSubscriber = new MessagingDiagnosticSourceSubscriber(
            diagnosticListener,
            isEnabledFilter: null
        );
        _diagnosticSourceSubscriber.Subscribe();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _diagnosticSourceSubscriber?.Dispose();
        _metrics?.Dispose();
    }
}
