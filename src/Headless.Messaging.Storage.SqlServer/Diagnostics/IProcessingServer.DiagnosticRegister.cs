// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Internal;

namespace Headless.Messaging.SqlServer.Diagnostics;

public sealed class DiagnosticRegister(DiagnosticProcessorObserver observer) : IProcessingServer
{
    private IDisposable? _subscription;

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        _subscription = DiagnosticListener.AllListeners.Subscribe(observer);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _subscription?.Dispose();
        }
    }
}
