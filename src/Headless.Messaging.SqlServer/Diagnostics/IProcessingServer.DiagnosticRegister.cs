// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Internal;

namespace Headless.Messaging.SqlServer.Diagnostics;

public class DiagnosticRegister(DiagnosticProcessorObserver diagnosticProcessorObserver) : IProcessingServer
{
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        DiagnosticListener.AllListeners.Subscribe(diagnosticProcessorObserver);

        return ValueTask.CompletedTask;
    }
}
