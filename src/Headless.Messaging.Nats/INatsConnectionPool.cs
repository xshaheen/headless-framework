// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace Headless.Messaging.Nats;

public interface INatsConnectionPool
{
    string ServersAddress { get; }

    IConnection RentConnection();

    bool Return(IConnection connection);
}

public sealed class NatsConnectionPool : INatsConnectionPool, IDisposable
{
    private readonly MessagingNatsOptions _options;
    private readonly ConcurrentQueue<IConnection> _connectionPool;
    private readonly ConnectionFactory _connectionFactory;
    private readonly int _maxSize;

    // Tracks the number of connections currently queued in the pool (not outstanding/rented).
    private int _pCount;
    private int _disposed;

    public NatsConnectionPool(ILogger<NatsConnectionPool> logger, IOptions<MessagingNatsOptions> options)
    {
        _options = options.Value;
        _connectionPool = new ConcurrentQueue<IConnection>();
        _connectionFactory = new ConnectionFactory();
        _maxSize = _options.ConnectionPoolSize;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("NATS configuration: {Options}", options.Value.Options);
        }
    }

    public string ServersAddress => _options.Servers;

    public IConnection RentConnection()
    {
        if (_connectionPool.TryDequeue(out var connection))
        {
            Interlocked.Decrement(ref _pCount);
            return connection;
        }

        if (_options.Options is not null)
        {
            // Create a fresh options copy to avoid mutating the shared instance
            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = _options.Servers;
            return _connectionFactory.CreateConnection(opts);
        }

        return _connectionFactory.CreateConnection(_options.Servers);
    }

    public bool Return(IConnection connection)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (!connection.IsReconnecting())
                connection.Dispose();
            return false;
        }

        if (connection.State != ConnState.CONNECTED)
        {
            if (!connection.IsReconnecting())
                connection.Dispose();
            return false;
        }

        // Atomic check-and-increment: only enqueue if pool is not full
        while (true)
        {
            var current = Volatile.Read(ref _pCount);
            if (current >= _maxSize)
            {
                if (!connection.IsReconnecting())
                    connection.Dispose();
                return false;
            }

            if (Interlocked.CompareExchange(ref _pCount, current + 1, current) == current)
            {
                _connectionPool.Enqueue(connection);
                return true;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        while (_connectionPool.TryDequeue(out var context))
        {
            context.Dispose();
        }
    }
}
