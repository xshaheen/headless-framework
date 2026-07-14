// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal interface IRedisPubSubConnectionProvider : IAsyncDisposable
{
    Task<IConnectionMultiplexer> ConnectAsync(CancellationToken cancellationToken = default);
}

internal sealed class RedisPubSubConnectionProvider(IOptions<RedisPubSubMessagingOptions> optionsAccessor)
    : IRedisPubSubConnectionProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ConnectionMultiplexer? _connection;
    private Task<ConnectionMultiplexer>? _connectionTask;
    private int _disposed;

    public async Task<IConnectionMultiplexer> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_connection is not null)
        {
            return _connection;
        }

        Task<ConnectionMultiplexer> connectionTask;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

#pragma warning disable CA1508 // Justification: other threads can initialize it while waiting for the lock.
            if (_connection is not null)
            {
                return _connection;
            }

            if (_connectionTask?.IsFaulted != false || _connectionTask.IsCanceled)
            {
                _connectionTask = ConnectionMultiplexer.ConnectAsync(optionsAccessor.Value.Configuration!);
            }

            connectionTask = _connectionTask;
#pragma warning restore CA1508
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            var connection = await connectionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            _connection = connection;
        }
        catch when (connectionTask.IsFaulted || connectionTask.IsCanceled)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);

                try
                {
                    if (ReferenceEquals(_connectionTask, connectionTask))
                    {
                        _connectionTask = null;
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }

            throw;
        }

        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ConnectionMultiplexer? connection;
        Task<ConnectionMultiplexer>? connectionTask;

        await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            connection = _connection;
            connectionTask = _connectionTask;
            _connection = null;
            _connectionTask = null;
        }
        finally
        {
            _lock.Release();
        }

        _lock.Dispose();

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            return;
        }

        if (connectionTask is null)
        {
            return;
        }

        try
        {
            connection = await connectionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (RedisException)
        {
            return;
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
