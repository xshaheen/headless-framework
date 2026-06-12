// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.CommitCoordination.SqlServer;

internal sealed class SqlServerCommitDiagnosticProbe(IOptions<SqlServerCommitCoordinationOptions> options)
    : ISqlServerCommitDiagnosticProbe
{
    public async ValueTask<SqlServerCommitDiagnosticProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var currentOptions = options.Value;
        var factory = currentOptions.DiagnosticProbeConnectionFactory;

        if (factory is null)
        {
            return SqlServerCommitDiagnosticProbeResult.Failure(
                "SQL Server commit diagnostic self-probe did not run because no probe connection factory is configured."
            );
        }

        if (currentOptions.DiagnosticProbeTimeout <= TimeSpan.Zero)
        {
            return SqlServerCommitDiagnosticProbeResult.Failure(
                "SQL Server commit diagnostic self-probe timeout must be positive."
            );
        }

        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(currentOptions.DiagnosticProbeTimeout);
        var probeToken = probeCts.Token;

        try
        {
            await using var connection = await factory(probeToken).ConfigureAwait(false);
            var shouldClose = connection.State == ConnectionState.Closed;
            using var listener = new ProbeDiagnosticListener(connection);
            using var subscription = DiagnosticListener.AllListeners.Subscribe(listener);

            if (shouldClose)
            {
                await connection.OpenAsync(probeToken).ConfigureAwait(false);
            }

            try
            {
                var transaction = (SqlTransaction)
                    await connection.BeginTransactionAsync(probeToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await transaction.CommitAsync(probeToken).ConfigureAwait(false);
                }

                await listener.WaitForCommitAsync(probeToken).ConfigureAwait(false);

                return SqlServerCommitDiagnosticProbeResult.Success(
                    "SQL Server commit diagnostic self-probe observed the expected commit diagnostic payload."
                );
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException ex) when (
            !cancellationToken.IsCancellationRequested && probeCts.IsCancellationRequested
        )
        {
            return SqlServerCommitDiagnosticProbeResult.Failure(
                "SQL Server commit diagnostic self-probe timed out before observing the expected diagnostic payload.",
                ex
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SqlServerCommitDiagnosticProbeResult.Failure(
                "SQL Server commit diagnostic self-probe failed.",
                ex
            );
        }
    }

    private sealed class ProbeDiagnosticListener(SqlConnection connection)
        : IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>,
            IDisposable
    {
        private readonly TaskCompletionSource _committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IDisposable> _subscriptions = [];

        public void OnCompleted() { }

        public void OnError(Exception error) => _committed.TrySetException(error);

        public void OnNext(DiagnosticListener listener)
        {
            if (
                string.Equals(
                    listener.Name,
                    SqlServerCommitDiagnosticListenerObserver.DiagnosticListenerName,
                    StringComparison.Ordinal
                )
            )
            {
                _subscriptions.Add(
                    listener.Subscribe(
                        this,
                        static (eventName, _, _) => SqlServerCommitDiagnosticObserver.IsSupportedEvent(eventName)
                    )
                );
            }
        }

        public void OnNext(KeyValuePair<string, object?> evt)
        {
            if (
                evt.Key == SqlServerCommitDiagnosticObserver.SqlAfterCommitTransaction
                && SqlServerCommitDiagnosticObserver.TryGetClientConnectionId(evt, out var key)
                && key == connection.ClientConnectionId
                && !SqlServerCommitDiagnosticObserver.IsRollbackOperation(evt.Value)
            )
            {
                _committed.TrySetResult();
            }
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }

        public async Task WaitForCommitAsync(CancellationToken cancellationToken)
        {
            await _committed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
