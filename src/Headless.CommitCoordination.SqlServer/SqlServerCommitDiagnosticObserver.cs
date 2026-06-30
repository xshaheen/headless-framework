// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
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
/// the framework never sees. This signal is a low-latency acceleration hook, not a correctness mechanism: it depends
/// on undocumented SqlClient payload shapes and may be missed, delayed, or disabled, so deferred work must remain
/// recoverable without it (a durable row committed in-transaction plus relay polling). A faulted or absent signal
/// degrades dispatch latency, never durability.
/// </summary>
/// <remarks>
/// SqlClient's diagnostic callbacks are synchronous <c>void</c> on the connection's own thread; blocking them on the
/// signal drain would stall the SqlClient pipeline. We therefore start the drain and let it run on the thread pool,
/// observing faults asynchronously (never swallowing them — a faulted drain is the relay's crash-recovery path: the
/// uncommitted work buffer is recovered by the relay on restart, so a logged fault here is a diagnostic, not data
/// loss). We do not <c>GetAwaiter().GetResult()</c> on the diagnostic thread.
/// </remarks>
internal sealed partial class SqlServerCommitDiagnosticObserver(
    SqlServerCommitSignalSource signalSource,
    ILogger<SqlServerCommitDiagnosticObserver> logger
) : IObserver<KeyValuePair<string, object?>>
{
    private readonly ILogger _logger = logger;
    private readonly ConcurrentDictionary<Task, byte> _drains = [];

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
                if (!TryGetClientConnectionId(evt, out var key))
                {
                    return;
                }

                // The commit-after event can carry a "Rollback" operation in some flows; treat it as a rollback.
                if (IsRollbackOperation(evt.Value))
                {
                    _Drain(signalSource.SignalRolledBackAsync(key, CancellationToken.None));
                }
                else
                {
                    _Drain(signalSource.SignalCommittedAsync(key, CancellationToken.None));
                }

                break;
            }
            case SqlErrorCommitTransaction or SqlAfterRollbackTransaction or SqlBeforeCloseConnection:
            {
                if (!TryGetClientConnectionId(evt, out var key))
                {
                    return;
                }

                _Drain(signalSource.SignalRolledBackAsync(key, CancellationToken.None));

                break;
            }
        }
    }

    internal static bool IsSupportedEvent(string eventName)
    {
        return eventName
            is SqlAfterCommitTransaction
                or SqlErrorCommitTransaction
                or SqlAfterRollbackTransaction
                or SqlBeforeCloseConnection;
    }

    internal async Task WaitForDrainsAsync(CancellationToken cancellationToken)
    {
        // Callers dispose the diagnostic subscription BEFORE awaiting this, so no new drains should arrive. Bound the
        // wait anyway: re-snapshot a few times to catch stragglers enqueued between the last observed event and the
        // unsubscribe completing, but never spin unboundedly if events are somehow still flowing — remaining work is
        // relay-recoverable, so a bounded wait that logs on overflow is safer than an open-ended loop.
        const int maxIterations = 3;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var drains = _drains.Keys.ToArray();

            if (drains.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(drains).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Drain faults are observed and logged by their continuations; shutdown only waits for completion.
                LogDrainFaulted(_logger, ex);
            }
        }

        if (!_drains.IsEmpty)
        {
            LogDrainsStillPendingAtShutdown(_logger, maxIterations);
        }
    }

    private void _Drain(ValueTask drain)
    {
        // Fast path: an uncoordinated transaction's signal completes synchronously AND successfully (no scope
        // attached) — IsCompletedSuccessfully is true, so skip the Task allocation entirely. A pending drain, or a
        // synchronously-FAULTED one (IsCompletedSuccessfully is false), falls through to AsTask() below, where the
        // continuation tracks it for the shutdown wait and observes/logs any fault — never silently swallowed.
        if (drain.IsCompletedSuccessfully)
        {
            return;
        }

        var drainTask = drain.AsTask();
        _drains.TryAdd(drainTask, 0);

        // Observe faults off the diagnostic thread; the relay recovers any uncommitted buffer on restart. The
        // cleanup continuation runs on the thread pool (no ExecuteSynchronously): TryAdd above already settled,
        // so a synchronously-completed drainTask cannot inline TryRemove ahead of it and leave a phantom entry.
        _ = drainTask.ContinueWith(
            static (t, state) =>
            {
                var self = (SqlServerCommitDiagnosticObserver)state!;
                self._drains.TryRemove(t, out _);

                if (t.IsFaulted)
                {
                    LogDrainFaulted(self._logger, t.Exception);
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default
        );
    }

    internal static bool TryGetClientConnectionId(KeyValuePair<string, object?> evt, out Guid clientConnectionId)
    {
        if (_GetProperty(evt.Value, "Connection") is SqlConnection sqlConnection)
        {
            clientConnectionId = sqlConnection.ClientConnectionId;

            return true;
        }

        clientConnectionId = Guid.Empty;

        return false;
    }

    internal static bool IsRollbackOperation(object? payload)
    {
        return _GetProperty(payload, "Operation") as string == "Rollback";
    }

    private static object? _GetProperty(object? source, string propertyName)
    {
        if (source is null)
        {
            return null;
        }

        var type = source.GetType();
        var cache = _PropertyCache.GetValue(type, static _ => new ConcurrentPropertyCache());
        var accessor = cache.GetOrAdd(propertyName, type);

        return accessor?.Invoke(source);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A SQL Server commit coordination signal drain faulted; the relay will recover any uncommitted work."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDrainFaulted(ILogger logger, Exception? exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "SQL Server commit coordination drains were still pending after {Iterations} shutdown wait iterations; "
            + "abandoning the wait — remaining work is relay-recoverable."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDrainsStillPendingAtShutdown(ILogger logger, int iterations);

    private sealed class ConcurrentPropertyCache
    {
        private readonly ConcurrentDictionary<string, Func<object, object?>?> _properties = new(StringComparer.Ordinal);

        public Func<object, object?>? GetOrAdd(string propertyName, Type type)
        {
            return _properties.GetOrAdd(propertyName, static (name, t) => _CompileGetter(t, name), type);
        }

        private static Func<object, object?>? _CompileGetter(Type type, string propertyName)
        {
            var property = type.GetTypeInfo().GetDeclaredProperty(propertyName);

            if (property?.GetMethod is null)
            {
                return null;
            }

            var source = Expression.Parameter(typeof(object), "source");
            var typedSource = Expression.Convert(source, type);
            var propertyAccess = Expression.Property(typedSource, property);
            var boxed = Expression.Convert(propertyAccess, typeof(object));

            return Expression.Lambda<Func<object, object?>>(boxed, source).Compile();
        }
    }
}
