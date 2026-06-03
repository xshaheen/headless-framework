// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

#pragma warning disable VSTHRD003, VSTHRD110, MA0134, ERP022
// The LISTEN loop is an owned background receiver. Disposal cancels and observes it; notification
// fanout is best-effort and fault-observed through a continuation so the listener is never blocked
// by a waiter callback.
internal sealed class PostgresReleaseSignal : IReleaseSignal, IAsyncDisposable
{
    private const string _Channel = "headless_distributed_locks_release";
    private static readonly TimeSpan _MaxReconnectBackoff = TimeSpan.FromSeconds(30);
    private readonly PollingReleaseSignal _local;
    private readonly TimeProvider _timeProvider;
    private readonly NpgsqlDataSource? _ownedDataSource;
    private readonly NpgsqlDataSource? _dataSource;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly Task? _listenerTask;
    private readonly int _commandTimeoutSeconds;

    public PostgresReleaseSignal(IOptions<PostgresDistributedLockOptions> options, TimeProvider timeProvider)
    {
        Options = options.Value;
        _timeProvider = timeProvider;
        _local = new PollingReleaseSignal(timeProvider);
        _commandTimeoutSeconds = (int)Options.CommandTimeout.TotalSeconds;

        if (Options.EnablePushWakeup)
        {
            _dataSource = Options.DataSource ?? (_ownedDataSource = NpgsqlDataSource.Create(Options.ConnectionString!));
            _listenerTask = Task.Run(_ListenAsync);
        }
    }

    private PostgresDistributedLockOptions Options { get; }

    public async ValueTask WaitAsync(
        string resource,
        TimeSpan pollingFallback,
        CancellationToken cancellationToken = default
    )
    {
        await _local.WaitAsync(resource, pollingFallback, cancellationToken).ConfigureAwait(false);
    }

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

    public async ValueTask DisposeAsync()
    {
        await _disposeTokenSource.CancelAsync().ConfigureAwait(false);

        if (_listenerTask is not null)
        {
            await _listenerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _disposeTokenSource.Dispose();

        if (_ownedDataSource is not null)
        {
            await _ownedDataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task _ListenAsync()
    {
        var cancellationToken = _disposeTokenSource.Token;
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await _dataSource!.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                connection.Notification += OnNotification;

                await using (var listen = connection.CreateCommand())
                {
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
            catch
            {
                // Exponential backoff with jitter so a PG restart does not trigger a synchronized
                // reconnect storm across every instance: min(1s * 2^n, 30s) * [0.8, 1.2).
                var exponential = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, consecutiveFailures), _MaxReconnectBackoff.TotalSeconds));
                consecutiveFailures++;
                var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
                var delay = TimeSpan.FromMilliseconds(exponential.TotalMilliseconds * jitter);
                await _timeProvider.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        void OnNotification(object sender, NpgsqlNotificationEventArgs args)
        {
            _local
                .PublishAsync(args.Payload, CancellationToken.None)
                .AsTask()
                .ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default
                );
        }
    }

    private async ValueTask<NpgsqlConnection> _OpenConnectionAsync(CancellationToken cancellationToken)
    {
        // Only reachable when EnablePushWakeup is true, which guarantees _dataSource was assigned
        // (either the configured DataSource or the owned one created from ConnectionString).
        return await _dataSource!.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }
}
#pragma warning restore VSTHRD003, VSTHRD110, MA0134, ERP022
