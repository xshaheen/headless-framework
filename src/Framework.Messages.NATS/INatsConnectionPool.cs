// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace Framework.Messages;

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
    private int _pCount;
    private int _maxSize;

    public NatsConnectionPool(ILogger<NatsConnectionPool> logger, IOptions<MessagingNatsOptions> options)
    {
        _options = options.Value;
        _connectionPool = new ConcurrentQueue<IConnection>();
        _connectionFactory = new ConnectionFactory();
        _maxSize = _options.ConnectionPoolSize;
        logger.LogDebug("NATS configuration: {Options}", options.Value.Options);
    }

    public string ServersAddress => _options.Servers;

    public IConnection RentConnection()
    {
        if (_connectionPool.TryDequeue(out var connection))
        {
            Interlocked.Decrement(ref _pCount);

            return connection;
        }

        if (_options.Options != null)
        {
            _options.Options.Url = _options.Servers;
            connection = _connectionFactory.CreateConnection(_options.Options);
        }
        else
        {
            connection = _connectionFactory.CreateConnection(_options.Servers);
        }

        return connection;
    }

    public bool Return(IConnection connection)
    {
        if (Interlocked.Increment(ref _pCount) <= _maxSize && connection.State == ConnState.CONNECTED)
        {
            _connectionPool.Enqueue(connection);

            return true;
        }

        if (!connection.IsReconnecting())
        {
            connection.Dispose();
        }

        Interlocked.Decrement(ref _pCount);

        return false;
    }

    public void Dispose()
    {
        _maxSize = 0;

        while (_connectionPool.TryDequeue(out var context))
        {
            context.Dispose();
        }
    }
}
