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

        // Hoisted so the timeout catch can read whether a commit event arrived with an unreadable payload.
        ProbeDiagnosticListener? listener = null;

        try
        {
            await using var connection = await factory(probeToken).ConfigureAwait(false);
            var shouldClose = connection.State == ConnectionState.Closed;
            listener = new ProbeDiagnosticListener(connection);
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
                    "SQL Server commit diagnostic self-probe observed the expected commit diagnostic payload, including a "
                        + "resolvable ClientConnectionId."
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
        catch (OperationCanceledException ex)
            when (!cancellationToken.IsCancellationRequested && probeCts.IsCancellationRequested)
        {
            // Distinguish payload-shape drift from a genuinely absent event: if the commit-after event fired for the
            // probe transaction but ClientConnectionId could not be read from its payload, the SqlClient diagnostic
            // contract has likely changed — out-of-band correlation would silently break. Surface that precisely
            // instead of a generic timeout.
            return listener?.UnresolvedPayloadType is { } payloadType
                ? SqlServerCommitDiagnosticProbeResult.Failure(
                    "SQL Server commit diagnostic self-probe observed the commit event but could not read "
                        + $"ClientConnectionId from payload type '{payloadType}'; the SqlClient diagnostic payload "
                        + "shape may have changed, which would disable out-of-band commit detection.",
                    ex
                )
                : SqlServerCommitDiagnosticProbeResult.Failure(
                    "SQL Server commit diagnostic self-probe timed out before observing the expected diagnostic payload.",
                    ex
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SqlServerCommitDiagnosticProbeResult.Failure("SQL Server commit diagnostic self-probe failed.", ex);
        }
        finally
        {
            // Disposed here (not via `using`) because the listener is hoisted so the timeout catch can read it.
            listener?.Dispose();
        }
    }

    private sealed class ProbeDiagnosticListener(SqlConnection connection)
        : IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>,
            IDisposable
    {
        private readonly TaskCompletionSource _committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IDisposable> _subscriptions = [];

        /// <summary>
        /// Set when a commit-after event fired for this connection but its payload did not yield a ClientConnectionId
        /// — the signature of a SqlClient diagnostic payload-shape change. Read after a probe timeout to report drift.
        /// </summary>
        public string? UnresolvedPayloadType { get; private set; }

        public void OnCompleted() { }

        public void OnError(Exception error)
        {
            _committed.TrySetException(error);
        }

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
                !string.Equals(
                    evt.Key,
                    SqlServerCommitDiagnosticObserver.SqlAfterCommitTransaction,
                    StringComparison.Ordinal
                ) || SqlServerCommitDiagnosticObserver.IsRollbackOperation(evt.Value)
            )
            {
                return;
            }

            if (SqlServerCommitDiagnosticObserver.TryGetClientConnectionId(evt, out var key))
            {
                if (key == connection.ClientConnectionId)
                {
                    _committed.TrySetResult();
                }
            }
            else
            {
                // The commit-after event fired but its payload yielded no ClientConnectionId — record the payload
                // type so a probe timeout can report payload-shape drift instead of a generic "event never fired".
                UnresolvedPayloadType = evt.Value?.GetType().FullName ?? "<null>";
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
