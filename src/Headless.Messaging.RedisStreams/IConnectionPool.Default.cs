// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.RedisStreams;

internal class RedisConnectionPool : IRedisConnectionPool, IDisposable
{
    private readonly ConcurrentBag<AsyncLazyRedisConnection> _connections = [];

    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _poolLock = new(1);
    private readonly MessagingRedisOptions _redisOptions;
    private bool _isDisposed;
    private bool _poolAlreadyConfigured;

    public RedisConnectionPool(IOptions<MessagingRedisOptions> options, ILoggerFactory loggerFactory)
    {
        _redisOptions = options.Value;
        _loggerFactory = loggerFactory;
        _Init().GetAwaiter().GetResult();
    }

    private AsyncLazyRedisConnection? QuietConnection
    {
        get
        {
            return _poolAlreadyConfigured
                ? _connections.OrderBy(static c => c.CreatedConnection?.ConnectionCapacity ?? int.MaxValue).First()
                : null;
        }
    }

    public void Dispose()
    {
        _Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async Task<IConnectionMultiplexer> ConnectAsync()
    {
        if (QuietConnection == null)
        {
            _poolAlreadyConfigured =
                _connections.Count(static c => c.IsValueCreated) == _redisOptions.ConnectionPoolSize;
            if (QuietConnection != null)
            {
                return QuietConnection.CreatedConnection!.Connection;
            }
        }

        foreach (var lazy in _connections)
        {
            if (!lazy.IsValueCreated)
            {
                return (await lazy).Connection;
            }

            if (lazy.CreatedConnection!.ConnectionCapacity == default)
            {
                return lazy.CreatedConnection.Connection;
            }
        }

        return (await _connections.OrderBy(static c => c.CreatedConnection!.ConnectionCapacity).First()).Connection;
    }

    private async Task _Init()
    {
        try
        {
            await _poolLock.WaitAsync();

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

    private void _Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (var connection in _connections)
            {
                if (!connection.IsValueCreated)
                {
                    continue;
                }

                connection.CreatedConnection!.Dispose();
            }

            _poolLock.Dispose();
        }

        _isDisposed = true;
    }

    ~RedisConnectionPool()
    {
        if (!_isDisposed)
        {
            System.Diagnostics.Debug.Fail(
                "RedisConnectionPool was not disposed. Call Dispose() to release SemaphoreSlim."
            );
        }
    }
}
