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
public sealed class SqlServerCommitDiagnosticHostedService : IHostedService, IDisposable
{
    private readonly SqlServerCommitDiagnosticListenerObserver _listenerObserver;
    private readonly SqlServerCommitDiagnosticObserver _observer;
    private IDisposable? _subscription;

    public SqlServerCommitDiagnosticHostedService(
        SqlServerCommitDiagnosticListenerObserver listenerObserver,
        SqlServerCommitDiagnosticObserver observer
    )
    {
        _listenerObserver = listenerObserver;
        _observer = observer;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription ??= DiagnosticListener.AllListeners.Subscribe(_listenerObserver);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        _listenerObserver.Dispose();

        await _observer.WaitForDrainsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _listenerObserver.Dispose();
    }
}
