// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace Headless.Messaging.Nats;

public interface INatsConnectionPool : IAsyncDisposable
{
    string ServersAddress { get; }

    NatsConnection GetConnection();
}

public sealed class NatsConnectionPool : INatsConnectionPool
{
    private readonly NatsConnection[] _connections;
    private int _disposed;
    private int _index;

    public NatsConnectionPool(ILogger<NatsConnectionPool> logger, IOptions<MessagingNatsOptions> options)
    {
        var opts = options.Value;
        ServersAddress = BrokerAddressDisplay.FormatMany(opts.Servers);

        var natsOpts = opts.BuildNatsOpts();
        var poolSize = opts.ConnectionPoolSize;
        _connections = new NatsConnection[poolSize];

        for (var i = 0; i < poolSize; i++)
        {
            _connections[i] = new NatsConnection(natsOpts);
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "NATS connection pool created with {PoolSize} connections to {Servers}.",
                poolSize,
                ServersAddress
            );
        }
    }

    public string ServersAddress { get; }

    /// <summary>
    /// Returns a connection from the pool using round-robin distribution.
    /// Connections are long-lived and multiplexed — no return is needed.
    /// </summary>
    public NatsConnection GetConnection()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var index = Interlocked.Increment(ref _index);
        return _connections[(index & 0x7FFF_FFFF) % _connections.Length];
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var connection in _connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
