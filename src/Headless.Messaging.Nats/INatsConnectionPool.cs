// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace Headless.Messaging.Nats;

/// <summary>
/// Manages a fixed pool of <c>NatsConnection</c> instances for the NATS JetStream transport.
/// </summary>
/// <remarks>
/// Connections are long-lived and multiplexed; they are not rented or returned. Instead,
/// <see cref="GetConnection"/> selects a connection using round-robin distribution.
/// </remarks>
internal interface INatsConnectionPool : IAsyncDisposable
{
    /// <summary>Gets the formatted NATS server addresses used by this pool.</summary>
    string ServersAddress { get; }

    /// <summary>
    /// Gets the options of the pooled connections. Consumer clients open their own connections from these, so a
    /// connection the application supplied through <see cref="NatsMessagingOptions.UseConnection"/> also decides the
    /// servers and credentials consumers use.
    /// </summary>
    NatsOpts ConnectionOpts { get; }

    /// <summary>
    /// Returns a connection from the pool using round-robin distribution. The returned connection
    /// is shared and long-lived; do not dispose it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    INatsConnection GetConnection();
}

/// <summary>Default implementation of <see cref="INatsConnectionPool"/>.</summary>
/// <remarks>
/// Internal implementation detail: consumers resolve <see cref="INatsConnectionPool"/> from DI and
/// never reference this concrete type. Kept <see langword="internal"/> to stay off the package's public surface.
/// </remarks>
internal sealed class NatsConnectionPool : INatsConnectionPool
{
    private readonly INatsConnection[] _connections;

    // False when the application supplied the connection: it owns that connection's lifetime, not the pool.
    private readonly bool _ownsConnections;
    private int _disposed;
    private int _index;

    public NatsConnectionPool(
        ILogger<NatsConnectionPool> logger,
        IOptions<NatsMessagingOptions> options,
        IServiceProvider? serviceProvider = null
    )
    {
        var opts = options.Value;

        if (opts.ConnectionFactory is { } connectionFactory)
        {
            var supplied =
                connectionFactory(
                    serviceProvider
                        ?? throw new InvalidOperationException(
                            "A service provider is required to resolve the connection supplied through UseConnection."
                        )
                ) ?? throw new InvalidOperationException("The UseConnection factory returned no NATS connection.");

            _connections = [supplied];
            _ownsConnections = false;
            ConnectionOpts = supplied.Opts;
            ServersAddress = BrokerAddressDisplay.FormatMany(supplied.Opts.Url);
        }
        else
        {
            var natsOpts = opts.BuildNatsOpts();
            var poolSize = opts.ConnectionPoolSize;
            _connections = new INatsConnection[poolSize];

            for (var i = 0; i < poolSize; i++)
            {
                _connections[i] = new NatsConnection(natsOpts);
            }

            _ownsConnections = true;
            ConnectionOpts = natsOpts;
            ServersAddress = BrokerAddressDisplay.FormatMany(opts.Servers);
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogNatsConnectionPoolCreated(_connections.Length, ServersAddress);
        }
    }

    public string ServersAddress { get; }

    public NatsOpts ConnectionOpts { get; }

    /// <summary>
    /// Eagerly connects all pooled connections to the NATS server.
    /// Call during startup to surface connection failures early instead of on first publish.
    /// </summary>
    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var connection in _connections)
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Returns a connection from the pool using round-robin distribution.
    /// Connections are long-lived and multiplexed, so no return is needed.
    /// </summary>
    public INatsConnection GetConnection()
    {
        Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

        var index = Interlocked.Increment(ref _index);
        return _connections[(index & 0x7FFF_FFFF) % _connections.Length];
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || !_ownsConnections)
        {
            return;
        }

        foreach (var connection in _connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal static partial class NatsConnectionPoolLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "NatsConnectionPoolCreated",
        Level = LogLevel.Debug,
        Message = "NATS connection pool created with {PoolSize} connections to {Servers}."
    )]
    public static partial void LogNatsConnectionPoolCreated(this ILogger logger, int poolSize, string servers);
}
