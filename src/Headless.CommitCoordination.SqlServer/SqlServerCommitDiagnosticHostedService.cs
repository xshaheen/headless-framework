// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Hosts the lifetime of the SqlClient diagnostic subscription: subscribes the
/// <see cref="SqlServerCommitDiagnosticListenerObserver" /> to <see cref="DiagnosticListener.AllListeners" /> on
/// start and disposes the subscription on stop.
/// </summary>
internal sealed class SqlServerCommitDiagnosticHostedService : IHostedService, IAsyncDisposable, IDisposable
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
    public async ValueTask DisposeAsync()
    {
        _subscription?.Dispose();
        _subscription = null;
        _listenerObserver.Dispose();

        // Mirror StopAsync: DI / host teardown that disposes this service without first calling StopAsync
        // (the async-aware container prefers DisposeAsync) still flushes in-flight commit-signal drains
        // instead of abandoning them mid-flight. CancellationToken.None — graceful disposal waits fully.
        await _observer.WaitForDrainsAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Synchronous teardown disposes the subscription only. In-flight drains are awaited by DisposeAsync
        // (the path the async-aware host/DI container uses) or by StopAsync; a blocking wait here would
        // reintroduce sync-over-async deadlock risk, so the sync path stays best-effort — any abandoned
        // drain remains relay-recoverable (durable row + polling sweep).
        _subscription?.Dispose();
        _subscription = null;
        _listenerObserver.Dispose();
    }
}
