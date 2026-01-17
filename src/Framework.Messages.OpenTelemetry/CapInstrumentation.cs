// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace DotNetCore.CAP.OpenTelemetry;

/// <summary>
/// CAP instrumentation.
/// </summary>
internal class CapInstrumentation : IDisposable
{
    private readonly DiagnosticSourceSubscriber? _diagnosticSourceSubscriber;

    public CapInstrumentation(DiagnosticListener diagnosticListener)
    {
        _diagnosticSourceSubscriber = new DiagnosticSourceSubscriber(diagnosticListener, null);
        _diagnosticSourceSubscriber.Subscribe();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _diagnosticSourceSubscriber?.Dispose();
    }
}
