// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

#pragma warning disable VSTHRD003, VSTHRD110, MA0134, ERP022
// The LISTEN loop is an owned background receiver. Disposal cancels and observes it; notification
// fanout is best-effort and fault-observed through a continuation so the listener is never blocked
// by a waiter callback.
/// <summary>
/// Implements <see cref="IReleaseSignal"/> using PostgreSQL <c>LISTEN/NOTIFY</c> so blocked acquirers
/// are woken promptly when a holder releases, rather than relying on the polling fallback alone.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="PostgresDistributedLockOptions.EnablePushWakeup"/> is <see langword="true"/>, a
/// background <see cref="Task"/> runs a persistent <c>LISTEN</c> loop on a dedicated connection from the
/// shared <see cref="NpgsqlDataSource"/>. Release notifications sent via <see cref="PublishAsync"/> call
/// <c>pg_notify</c> on the <c>headless_distributed_locks_release</c> channel. Each arriving notification
/// fans out to local waiters through the wrapped <see cref="PollingReleaseSignal"/>.
/// </para>
/// <para>
/// If the listener connection drops, the loop reconnects with exponential backoff and jitter (capped at 30
/// seconds) so a PostgreSQL restart does not produce a synchronized reconnect storm. During reconnect,
/// cross-process wake-ups degrade to the polling fallback interval; no acquirer is stuck.
/// </para>
/// <para>
/// <see cref="DisposeAsync"/> cancels the listener loop and awaits its completion. The shared data source
/// is not disposed here.
/// </para>
/// </remarks>
internal sealed class PostgresReleaseSignal : IReleaseSignal, IAsyncDisposable
{
    private const string _Channel = "headless_distributed_locks_release";
    private static readonly TimeSpan _MaxReconnectBackoff = TimeSpan.FromSeconds(30);
    private readonly PollingReleaseSignal _local;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PostgresReleaseSignal> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly Task? _listenerTask;
    private readonly int _commandTimeoutSeconds;

    /// <summary>
    /// Initializes the release signal, wrapping a local <see cref="PollingReleaseSignal"/> and, when
    /// <see cref="PostgresDistributedLockOptions.EnablePushWakeup"/> is set, starting a background
    /// LISTEN/NOTIFY listener that wakes waiters early.
    /// </summary>
    /// <param name="options">Provider options supplying command timeout and push-wakeup toggle.</param>
    /// <param name="dataSource">
    /// The shared <see cref="NpgsqlDataSource"/> injected by the DI registration. Not disposed here.
    /// </param>
    /// <param name="timeProvider">Time source used for reconnect backoff delays.</param>
    /// <param name="logger">Logger for listener reconnect and fanout-failure warnings.</param>
    public PostgresReleaseSignal(
        IOptions<PostgresDistributedLockOptions> options,
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider,
        ILogger<PostgresReleaseSignal> logger
    )
    {
        Options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _local = new PollingReleaseSignal(timeProvider);
        _commandTimeoutSeconds = (int)Options.CommandTimeout.TotalSeconds;
        // The data source is shared and owned by the DI registration; it is never disposed here.
        _dataSource = dataSource;

        if (Options.EnablePushWakeup)
        {
            _listenerTask = Task.Run(_ListenAsync);
        }
    }

    private PostgresDistributedLockOptions Options { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Delegates entirely to the wrapped <see cref="PollingReleaseSignal"/>. The background LISTEN loop
    /// feeds notifications into it so a push-based wake-up also completes this wait early.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the wait elapses.
    /// </exception>
    public async ValueTask WaitAsync(
        string resource,
        TimeSpan pollingFallback,
        CancellationToken cancellationToken = default
    )
    {
        await _local.WaitAsync(resource, pollingFallback, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When <see cref="PostgresDistributedLockOptions.EnablePushWakeup"/> is <see langword="true"/>,
    /// publishes a <c>pg_notify</c> on the <c>headless_distributed_locks_release</c> channel in addition
    /// to notifying local waiters. Cross-process waiters listening on the channel are woken promptly.
    /// Underlying Npgsql errors from the <c>pg_notify</c> command propagate to the caller.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the publish command completes.
    /// </exception>
    public async ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default)
    {
        await _local.PublishAsync(resource, cancellationToken).ConfigureAwait(false);

        if (!Options.EnablePushWakeup)
        {
            return;
        }

        await using var connection = await _OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _commandTimeoutSeconds;
        command.CommandText = $"SELECT pg_catalog.pg_notify('{_Channel}', @resource)";
        command.Parameters.AddWithValue("resource", resource);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels and awaits the background LISTEN loop (suppressing any faulted/cancelled exception), then
    /// disposes the cancellation token source. The shared <see cref="NpgsqlDataSource"/> is not disposed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _disposeTokenSource.CancelAsync().ConfigureAwait(false);

        if (_listenerTask is not null)
        {
            await _listenerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _disposeTokenSource.Dispose();
    }

    private async Task _ListenAsync()
    {
        var cancellationToken = _disposeTokenSource.Token;
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await _dataSource
                    .OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                connection.Notification += OnNotification;

                await using (var listen = connection.CreateCommand())
                {
                    listen.CommandTimeout = _commandTimeoutSeconds;
                    listen.CommandText = $"LISTEN {_Channel}";
                    await listen.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                // Listener is established; clear the backoff so a future transient failure restarts
                // from the base delay rather than the previous (possibly capped) interval.
                consecutiveFailures = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                connection.Notification -= OnNotification;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogReleaseListenerReconnecting(exception, consecutiveFailures + 1);

                // Exponential backoff with jitter so a PG restart does not trigger a synchronized
                // reconnect storm across every instance: min(1s * 2^n, 30s) * [0.8, 1.2).
                var exponential = TimeSpan.FromSeconds(
                    Math.Min(Math.Pow(2, consecutiveFailures), _MaxReconnectBackoff.TotalSeconds)
                );
                consecutiveFailures++;
                var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
                var delay = TimeSpan.FromMilliseconds(exponential.TotalMilliseconds * jitter);
                await _timeProvider.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        void OnNotification(object sender, NpgsqlNotificationEventArgs args)
        {
            var resource = args.Payload;

            _local
                .PublishAsync(resource, CancellationToken.None)
                .AsTask()
                .ContinueWith(
                    task => _logger.LogReleaseNotificationFanoutFailed(task.Exception!, resource),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default
                );
        }
    }

    private async ValueTask<NpgsqlConnection> _OpenConnectionAsync(CancellationToken cancellationToken)
    {
        return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }
}
#pragma warning restore VSTHRD003, VSTHRD110, MA0134, ERP022
