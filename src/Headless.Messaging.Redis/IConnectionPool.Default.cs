// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisConnectionPool : IRedisConnectionPool, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentBag<AsyncLazyRedisConnection> _connections = [];

    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _poolLock = new(1);
    private readonly RedisMessagingOptions _redisOptions;
    private int _isDisposed;
    private bool _poolAlreadyConfigured;

    public RedisConnectionPool(IOptions<RedisMessagingOptions> options, ILoggerFactory loggerFactory)
    {
        _redisOptions = options.Value;
        _loggerFactory = loggerFactory;
        _Init();
    }

    private AsyncLazyRedisConnection? QuietConnection =>
        _poolAlreadyConfigured
            ? _connections.OrderBy(static c => c.CreatedConnection?.ConnectionCapacity ?? int.MaxValue).FirstOrDefault()
            : null;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _DisposeCreatedConnections();
        _poolLock.Dispose();

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await _DisposeCreatedConnectionsAsync().ConfigureAwait(false);
        _poolLock.Dispose();

        GC.SuppressFinalize(this);
    }

    public async Task<IConnectionMultiplexer> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (QuietConnection is not { } quietConnection)
        {
            _poolAlreadyConfigured =
                _connections.Where(static c => c.IsValueCreated).Take(_redisOptions.ConnectionPoolSize + 1).Count()
                == _redisOptions.ConnectionPoolSize;
            quietConnection = QuietConnection;
            if (quietConnection?.CreatedConnection is { } createdConnection)
            {
                return createdConnection.Connection;
            }
        }
        else if (quietConnection.CreatedConnection is { } createdConnection)
        {
            return createdConnection.Connection;
        }

        foreach (var lazy in _connections)
        {
            if (!lazy.IsValueCreated)
            {
                return (await lazy.GetValueAsync(cancellationToken).ConfigureAwait(false)).Connection;
            }

            if (lazy.CreatedConnection is not { } createdConnection)
            {
                return (await lazy.GetValueAsync(cancellationToken).ConfigureAwait(false)).Connection;
            }

            if (createdConnection.ConnectionCapacity == 0)
            {
                return createdConnection.Connection;
            }
        }

        var selected = _connections
            .OrderBy(static c => c.CreatedConnection?.ConnectionCapacity ?? int.MaxValue)
            .First();

        return (await selected.GetValueAsync(cancellationToken).ConfigureAwait(false)).Connection;
    }

    private void _Init()
    {
        try
        {
            // _Init runs from the constructor, which cannot be async, so the pool lock is taken synchronously.
            // WaitAsync (MA0045) has no async caller to flow to here.
#pragma warning disable MA0045
            _poolLock.Wait();
#pragma warning restore MA0045

            if (!_connections.IsEmpty)
            {
                return;
            }

            for (var i = 0; i < _redisOptions.ConnectionPoolSize; i++)
            {
                var connection = new AsyncLazyRedisConnection(
                    _redisOptions,
                    _loggerFactory.CreateLogger<AsyncLazyRedisConnection>()
                );

                _connections.Add(connection);
            }
        }
        finally
        {
            _poolLock.Release();
        }
    }

    private void _DisposeCreatedConnections()
    {
        foreach (var connection in _connections)
        {
            if (!connection.IsValueCreated)
            {
                continue;
            }

            connection.CreatedConnection?.Dispose();
        }
    }

    private async ValueTask _DisposeCreatedConnectionsAsync()
    {
        foreach (var connection in _connections)
        {
            if (!connection.IsValueCreated)
            {
                continue;
            }

            RedisConnection createdConnection;

            try
            {
                createdConnection = await connection.GetValueAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (RedisException)
            {
                continue;
            }

            createdConnection.Dispose();
        }
    }

#pragma warning disable MA0055 // Dispose methods should call SuppressFinalize
    ~RedisConnectionPool()
#pragma warning restore MA0055
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            System.Diagnostics.Debug.Fail(
                "RedisConnectionPool was not disposed. Call Dispose() or DisposeAsync() to release resources."
            );
        }
    }
}
