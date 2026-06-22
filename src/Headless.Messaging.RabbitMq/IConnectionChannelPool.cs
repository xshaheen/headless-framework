// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// Manages a pool of AMQP channels over a single shared RabbitMQ connection.
/// </summary>
/// <remarks>
/// Channels are rented for publish operations and returned after use. The pool size is fixed at
/// 15 slots by default. Renting blocks when all slots are occupied until a channel is returned.
/// </remarks>
public interface IConnectionChannelPool
{
    /// <summary>Gets the broker host address in <c>host:port</c> form.</summary>
    string HostAddress { get; }

    /// <summary>
    /// Gets the effective exchange name, which includes the messaging version suffix when the
    /// configured version is not <c>"v1"</c>.
    /// </summary>
    string Exchange { get; }

    /// <summary>Returns the shared, lazily-established AMQP connection, opening it if necessary.</summary>
    Task<IConnection> GetConnectionAsync();

    /// <summary>
    /// Rents an AMQP channel from the pool, blocking until a slot is available.
    /// The caller must return the channel via <see cref="Return"/> when done.
    /// </summary>
    Task<IChannel> Rent();

    /// <summary>
    /// Rents an AMQP channel from the pool, blocking until a slot is available or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// The caller must return the channel via <see cref="Return"/> when done.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IChannel> Rent(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a previously rented channel to the pool. If the pool is full or the channel is
    /// closed, the channel is disposed instead.
    /// </summary>
    /// <param name="context">The channel to return.</param>
    /// <returns>
    /// <see langword="true"/> if the channel was returned to the pool;
    /// <see langword="false"/> if it was disposed because the pool was full or the channel was closed.
    /// </returns>
    bool Return(IChannel context);
}

/// <summary>Default implementation of <see cref="IConnectionChannelPool"/>.</summary>
public sealed class ConnectionChannelPool : IConnectionChannelPool, IDisposable, IAsyncDisposable
{
    private const int _DefaultPoolSize = 15;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private readonly Func<Task<IConnection>> _connectionActivator;
    private readonly bool _isPublishConfirms;
    private readonly ILogger<ConnectionChannelPool> _logger;
    private readonly ConcurrentQueue<IChannel> _pool;
    private readonly SemaphoreSlim _poolSemaphore;
    private IConnection? _connection;

    private int _count;
    private int _maxSize;

    public ConnectionChannelPool(
        ILogger<ConnectionChannelPool> logger,
        IOptions<MessagingOptions> messagingAccessorOptionsAccessor,
        IOptions<RabbitMqOptions> optionsAccessor
    )
    {
        _logger = logger;
        _maxSize = _DefaultPoolSize;
        _pool = new ConcurrentQueue<IChannel>();
        _poolSemaphore = new SemaphoreSlim(_DefaultPoolSize, _DefaultPoolSize);

        var messagingOptions = messagingAccessorOptionsAccessor.Value;
        var options = optionsAccessor.Value;

        _connectionActivator = _CreateConnection(options);
        _isPublishConfirms = options.PublishConfirms;

        HostAddress = $"{options.HostName}:{options.Port}";
        Exchange = string.Equals("v1", messagingOptions.Version, StringComparison.Ordinal)
            ? options.ExchangeName
            : $"{options.ExchangeName}.{messagingOptions.Version}";

        _logger.Configuration(
            options.HostName,
            options.Port,
            options.UserName,
            options.VirtualHost,
            options.ExchangeName
        );
    }

    Task<IChannel> IConnectionChannelPool.Rent() => ((IConnectionChannelPool)this).Rent(CancellationToken.None);

    // Acquires a pool slot from _poolSemaphore on the way in; the matching release happens in
    // IConnectionChannelPool.Return. The private _CreateChannelAsync helper below deliberately does NOT
    // touch the semaphore — it exists only for internal channel creation. Renting through the interface
    // (which is what RabbitMqTransport does) must go through this acquire so that every Rent is balanced
    // by a single Return release; otherwise Return over-releases and throws SemaphoreFullException once
    // the initial slot count is exceeded.
    async Task<IChannel> IConnectionChannelPool.Rent(CancellationToken cancellationToken)
    {
        await _poolSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await _CreateChannelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _poolSemaphore.Release();
            throw;
        }
    }

    bool IConnectionChannelPool.Return(IChannel connection)
    {
        try
        {
            return Return(connection);
        }
        finally
        {
            _poolSemaphore.Release();
        }
    }

    public string HostAddress { get; }

    public string Exchange { get; }

    public async Task<IConnection> GetConnectionAsync()
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection?.Dispose();
            _connection = await _connectionActivator().ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void Dispose()
    {
        _maxSize = 0;

        while (_pool.TryDequeue(out var channel))
        {
            channel.Dispose();
        }

        _connection?.Dispose();
        _poolSemaphore.Dispose();
        _connectionLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _maxSize = 0;

        while (_pool.TryDequeue(out var channel))
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _poolSemaphore.Dispose();
        _connectionLock.Dispose();
    }

    private static Func<Task<IConnection>> _CreateConnection(RabbitMqOptions options)
    {
        var factory = new ConnectionFactory
        {
            UserName = options.UserName,
            Port = options.Port,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            ClientProvidedName = Assembly.GetEntryAssembly()?.GetName().Name!.ToLower(CultureInfo.InvariantCulture),
        };

        if (options.HostName.Contains(',', StringComparison.Ordinal))
        {
            options.ConnectionFactoryOptions?.Invoke(factory);
            var endpoints = AmqpTcpEndpoint.ParseMultiple(options.HostName);
            foreach (var endpoint in endpoints)
            {
                endpoint.Ssl = factory.Ssl;
            }
            return () => factory.CreateConnectionAsync(endpoints);
        }

        factory.HostName = options.HostName;
        options.ConnectionFactoryOptions?.Invoke(factory);
        return () => factory.CreateConnectionAsync();
    }

    private async Task<IChannel> _CreateChannelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_pool.TryDequeue(out var model))
        {
            Interlocked.Decrement(ref _count);

            Debug.Assert(_count >= 0);

            return model;
        }

        try
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            model = await connection
                .CreateChannelAsync(new CreateChannelOptions(_isPublishConfirms, false), cancellationToken)
                .ConfigureAwait(false);
            await model
                .ExchangeDeclareAsync(
                    Exchange,
                    RabbitMqOptions.ExchangeType,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    passive: false,
                    noWait: false,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.ChannelModelCreateFailed(e);
            throw;
        }

        return model;
    }

    public bool Return(IChannel channel)
    {
        if (Interlocked.Increment(ref _count) <= _maxSize && channel.IsOpen)
        {
            _pool.Enqueue(channel);

            return true;
        }

        channel.Dispose();

        Interlocked.Decrement(ref _count);

        Debug.Assert(_maxSize == 0 || _pool.Count <= _maxSize);

        return false;
    }
}

internal static partial class ConnectionChannelPoolLog
{
    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Debug,
        Message = "RabbitMQ configuration:'HostName:{OptionsHostName}, Port:{OptionsPort}, UserName:{OptionsUserName}, VirtualHost:{OptionsVirtualHost}, ExchangeName:{OptionsExchangeName}'"
    )]
    public static partial void Configuration(
        this ILogger logger,
        string optionsHostName,
        int optionsPort,
        string? optionsUserName,
        string? optionsVirtualHost,
        string optionsExchangeName
    );

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error, Message = "RabbitMQ channel model create failed!")]
    public static partial void ChannelModelCreateFailed(this ILogger logger, Exception exception);
}
