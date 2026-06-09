// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Observes <c>SqlClientDiagnosticListener</c> events and turns the native commit/rollback edge of a SqlClient
/// transaction into a signal on <see cref="SqlServerCommitSignalSource" />, correlated by the connection's
/// <c>ClientConnectionId</c>. This is the out-of-band detector: it watches the real provider transaction boundary
/// rather than an application-driven <c>CommitAsync</c> call, so it observes the durable commit even on code paths
/// the framework never sees.
/// </summary>
/// <remarks>
/// SqlClient's diagnostic callbacks are synchronous <c>void</c> on the connection's own thread; blocking them on the
/// signal drain would stall the SqlClient pipeline. We therefore start the drain and let it run on the thread pool,
/// observing faults asynchronously (never swallowing them — a faulted drain is the relay's crash-recovery path: the
/// uncommitted work buffer is recovered by the relay on restart, so a logged fault here is a diagnostic, not data
/// loss). We do not <c>GetAwaiter().GetResult()</c> on the diagnostic thread.
/// </remarks>
[PublicAPI]
public sealed partial class SqlServerCommitDiagnosticObserver(
    SqlServerCommitSignalSource signalSource,
    ILogger<SqlServerCommitDiagnosticObserver> logger
) : IObserver<KeyValuePair<string, object?>>
{
    /// <summary>The SqlClient diagnostic event raised after a transaction commit completes.</summary>
    public const string SqlAfterCommitTransaction = "Microsoft.Data.SqlClient.WriteTransactionCommitAfter";

    /// <summary>The SqlClient diagnostic event raised when a transaction commit errors.</summary>
    public const string SqlErrorCommitTransaction = "Microsoft.Data.SqlClient.WriteTransactionCommitError";

    /// <summary>The SqlClient diagnostic event raised after a transaction rollback completes.</summary>
    public const string SqlAfterRollbackTransaction = "Microsoft.Data.SqlClient.WriteTransactionRollbackAfter";

    /// <summary>The SqlClient diagnostic event raised before a connection closes.</summary>
    public const string SqlBeforeCloseConnection = "Microsoft.Data.SqlClient.WriteConnectionCloseBefore";

    private static readonly ConditionalWeakTable<Type, ConcurrentPropertyCache> _PropertyCache = [];

    /// <inheritdoc />
    public void OnCompleted() { }

    /// <inheritdoc />
    public void OnError(Exception error) { }

    /// <inheritdoc />
    public void OnNext(KeyValuePair<string, object?> evt)
    {
        switch (evt.Key)
        {
            case SqlAfterCommitTransaction:
            {
                if (!_TryGetClientConnectionId(evt, out var key))
                {
                    return;
                }

                // The commit-after event can carry a "Rollback" operation in some flows; treat it as a rollback.
                if (_GetProperty(evt.Value, "Operation") as string == "Rollback")
                {
                    _Drain(signalSource.SignalRolledBackAsync(key, CancellationToken.None).AsTask());
                }
                else
                {
                    _Drain(signalSource.SignalCommittedAsync(key, CancellationToken.None).AsTask());
                }

                break;
            }
            case SqlErrorCommitTransaction
            or SqlAfterRollbackTransaction
            or SqlBeforeCloseConnection:
            {
                if (!_TryGetClientConnectionId(evt, out var key))
                {
                    return;
                }

                _Drain(signalSource.SignalRolledBackAsync(key, CancellationToken.None).AsTask());

                break;
            }
        }
    }

    private void _Drain(Task drain)
    {
        // Observe faults off the diagnostic thread; the relay recovers any uncommitted buffer on restart. The
        // continuation itself never faults, so its result is intentionally discarded.
        _ = drain.ContinueWith(
            static (t, state) => LogDrainFaulted((ILogger)state!, t.Exception),
            logger,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static bool _TryGetClientConnectionId(KeyValuePair<string, object?> evt, out Guid clientConnectionId)
    {
        if (_GetProperty(evt.Value, "Connection") is SqlConnection sqlConnection)
        {
            clientConnectionId = sqlConnection.ClientConnectionId;

            return true;
        }

        clientConnectionId = Guid.Empty;

        return false;
    }

    private static object? _GetProperty(object? source, string propertyName)
    {
        if (source is null)
        {
            return null;
        }

        var type = source.GetType();
        var cache = _PropertyCache.GetValue(type, static _ => new ConcurrentPropertyCache());
        var property = cache.GetOrAdd(propertyName, type);

        return property?.GetValue(source);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A SQL Server commit coordination signal drain faulted; the relay will recover any uncommitted work."
    )]
    private static partial void LogDrainFaulted(ILogger logger, Exception? exception);

    private sealed class ConcurrentPropertyCache
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PropertyInfo?> _properties = new(
            StringComparer.Ordinal
        );

        public PropertyInfo? GetOrAdd(string propertyName, Type type)
        {
            return _properties.GetOrAdd(
                propertyName,
                static (name, t) => t.GetTypeInfo().GetDeclaredProperty(name),
                type
            );
        }
    }
}
