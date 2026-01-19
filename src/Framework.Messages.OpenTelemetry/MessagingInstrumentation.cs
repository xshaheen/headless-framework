// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

/// <summary>
/// CAP instrumentation.
/// </summary>
internal sealed class MessagingInstrumentation : IDisposable
{
    private readonly MessagingDiagnosticSourceSubscriber? _diagnosticSourceSubscriber;

    public MessagingInstrumentation(DiagnosticListener diagnosticListener)
    {
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
    }
}
