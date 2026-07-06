// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Hosts the lifetime of the SqlClient diagnostic subscription: subscribes the
/// <see cref="SqlServerCommitDiagnosticListenerObserver" /> to <see cref="DiagnosticListener.AllListeners" /> on
/// start and disposes the subscription on stop.
/// </summary>
internal sealed partial class SqlServerCommitDiagnosticHostedService(
    SqlServerCommitDiagnosticListenerObserver listenerObserver,
    SqlServerCommitDiagnosticObserver observer,
    ISqlServerCommitDiagnosticProbe probe,
    SqlServerCommitDiagnosticProbeState probeState,
    IOptions<SqlServerCommitCoordinationOptions> options,
    ILogger<SqlServerCommitDiagnosticHostedService>? logger = null
) : IHostedService, IAsyncDisposable, IDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<SqlServerCommitDiagnosticHostedService>.Instance;
    private IDisposable? _subscription;
    private int _probeRan;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription ??= DiagnosticListener.AllListeners.Subscribe(listenerObserver);

        await _RunProbeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        listenerObserver.Dispose();

        await observer.WaitForDrainsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _subscription?.Dispose();
        _subscription = null;
        listenerObserver.Dispose();

        // Mirror StopAsync: DI / host teardown that disposes this service without first calling StopAsync
        // (the async-aware container prefers DisposeAsync) still flushes in-flight commit-signal drains
        // instead of abandoning them mid-flight. CancellationToken.None — graceful disposal waits fully.
        await observer.WaitForDrainsAsync(CancellationToken.None).ConfigureAwait(false);
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
        listenerObserver.Dispose();
    }

    private async ValueTask _RunProbeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _probeRan, 1) == 1)
        {
            return;
        }

        var mode = options.Value.DiagnosticProbeMode;

        if (mode == CommitProbeMode.Disabled)
        {
            probeState.MarkSkipped("SQL Server commit diagnostic self-probe is disabled.");

            return;
        }

        var result = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            probeState.MarkSucceeded(result.Message);

            return;
        }

        if (mode == CommitProbeMode.Strict)
        {
            probeState.MarkFailed(result.Message, result.Exception);
            LogDiagnosticProbeFailedStrict(_logger, result.Exception, result.Message);

            throw new InvalidOperationException(result.Message, result.Exception);
        }

        probeState.MarkDegraded(result.Message, result.Exception);
        LogDiagnosticProbeDegraded(_logger, result.Exception, result.Message);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "SQL Server commit coordination diagnostic self-probe is degraded: {Message}"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDiagnosticProbeDegraded(ILogger logger, Exception? exception, string message);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "SQL Server commit coordination diagnostic self-probe failed in strict mode: {Message}"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDiagnosticProbeFailedStrict(ILogger logger, Exception? exception, string message);
}
