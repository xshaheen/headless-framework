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
    private readonly PollingReleaseSignal _local;
    private readonly TimeProvider _timeProvider;
    private readonly NpgsqlDataSource? _ownedDataSource;
    private readonly NpgsqlDataSource? _dataSource;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly Task? _listenerTask;

    public PostgresReleaseSignal(IOptions<PostgresDistributedLockOptions> options, TimeProvider timeProvider)
    {
        Options = options.Value;
        _timeProvider = timeProvider;
        _local = new PollingReleaseSignal(timeProvider);

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
                await _timeProvider.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
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
        if (Options.DataSource is { } configuredDataSource)
        {
            return await configuredDataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_ownedDataSource is not null)
        {
            return await _ownedDataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        var connection = new NpgsqlConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}
#pragma warning restore VSTHRD003, VSTHRD110, MA0134, ERP022
