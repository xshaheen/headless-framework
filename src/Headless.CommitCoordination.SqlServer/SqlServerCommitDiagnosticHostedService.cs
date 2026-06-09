// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Hosts the lifetime of the SqlClient diagnostic subscription: subscribes the
/// <see cref="SqlServerCommitDiagnosticListenerObserver" /> to <see cref="DiagnosticListener.AllListeners" /> on
/// start and disposes the subscription on stop.
/// </summary>
[PublicAPI]
public sealed class SqlServerCommitDiagnosticHostedService(SqlServerCommitDiagnosticListenerObserver listenerObserver)
    : IHostedService, IDisposable
{
    private IDisposable? _subscription;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription ??= DiagnosticListener.AllListeners.Subscribe(listenerObserver);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
