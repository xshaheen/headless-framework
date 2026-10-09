// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
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
internal interface IConnectionChannelPool
{
    /// <summary>Gets the broker host address in <c>host:port</c> form.</summary>
    string HostAddress { get; }

    /// <summary>
    /// Gets the effective exchange name, which includes the messaging version suffix when the
    /// configured version is not <c>"v1"</c>.
    /// </summary>
    string Exchange { get; }

    /// <summary>Returns the shared, lazily-established AMQP connection, opening it if necessary.</summary>
    /// <param name="cancellationToken">Token to cancel connection setup.</param>
    Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a new AMQP connection that the caller owns and disposes, with the client library's automatic recovery
    /// turned off.
    /// </summary>
    /// <remarks>
    /// An every-instance consumer holds its exclusive queue on a connection of its own: the broker deletes the queue
    /// when that connection closes, and a lost connection stays lost so the messaging core rebuilds the consumer and
    /// raises its re-established signal. Automatic topology recovery would instead re-declare the server-named queue
    /// under a new name without telling anyone.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel connection setup.</param>
    Task<IConnection> CreateNonRecoveringConnectionAsync(CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Declares and binds the Queue-lane queue of <paramref name="messageName"/>, with the arguments its consumers
    /// declare it with, once per process; with <see cref="RabbitMqMessagingOptions.AutoProvision"/> off it proves the
    /// queue exists. A failed attempt is forgotten, so the next call tries again.
    /// </summary>
    /// <remarks>
    /// A Queue-lane message goes to a direct exchange, which drops a message no queue is bound for. Consumers declare
    /// the queue too, but a message sent before the first consumer ever started would be lost without this.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task EnsureQueueForPublishAsync(string messageName, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets that the Queue-lane queue of <paramref name="messageName"/> was ensured, so the next publish declares it
    /// again; used after the broker returned a message as unroutable, as it does once an operator deletes the queue.
    /// </summary>
    void ForgetQueueForPublish(string messageName);
}

/// <summary>Default implementation of <see cref="IConnectionChannelPool"/>.</summary>
internal sealed class ConnectionChannelPool : IConnectionChannelPool, IDisposable, IAsyncDisposable
{
    private const int _DefaultPoolSize = 15;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private readonly Func<CancellationToken, Task<IConnection>> _connectionActivator;
    private readonly Func<CancellationToken, Task<IConnection>> _nonRecoveringConnectionActivator;
    private readonly bool _isPublishConfirms;
    private readonly RabbitMqMessagingOptions _options;

    // One ensure per Queue-lane queue per process. The Lazy keeps concurrent first publishes on one declare; a failed
    // declare is removed, so the next publish tries again.
    private readonly ConcurrentDictionary<string, Lazy<Task>> _publishQueues = new(StringComparer.Ordinal);
    private readonly ILogger<ConnectionChannelPool> _logger;
    private readonly ConcurrentQueue<IChannel> _pool;
    private readonly SemaphoreSlim _poolSemaphore;
    private IConnection? _connection;

    private int _count;
    private int _maxSize;

    public ConnectionChannelPool(
        ILogger<ConnectionChannelPool> logger,
        IOptions<MessagingOptions> messagingAccessorOptionsAccessor,
        IOptions<RabbitMqMessagingOptions> optionsAccessor
    )
        : this(logger, messagingAccessorOptionsAccessor, optionsAccessor, connectionActivator: null) { }

    internal ConnectionChannelPool(
        ILogger<ConnectionChannelPool> logger,
        IOptions<MessagingOptions> messagingAccessorOptionsAccessor,
        IOptions<RabbitMqMessagingOptions> optionsAccessor,
        Func<CancellationToken, Task<IConnection>>? connectionActivator
    )
    {
        _logger = logger;
        _maxSize = _DefaultPoolSize;
        _pool = new ConcurrentQueue<IChannel>();
        _poolSemaphore = new SemaphoreSlim(_DefaultPoolSize, _DefaultPoolSize);

        var messagingOptions = messagingAccessorOptionsAccessor.Value;
        var options = optionsAccessor.Value;

        _connectionActivator = connectionActivator ?? _CreateConnection(options, automaticRecovery: true);
        _nonRecoveringConnectionActivator = connectionActivator ?? _CreateConnection(options, automaticRecovery: false);
        _isPublishConfirms = options.PublishConfirms;
        _options = options;

        HostAddress = string.Create(CultureInfo.InvariantCulture, $"{options.HostName}:{options.Port}");
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

    Task<IChannel> IConnectionChannelPool.Rent()
    {
        return ((IConnectionChannelPool)this).Rent(CancellationToken.None);
    }

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

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection?.Dispose();
            _connection = await _connectionActivator(cancellationToken).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public Task<IConnection> CreateNonRecoveringConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _nonRecoveringConnectionActivator(cancellationToken);
    }

    public Task EnsureQueueForPublishAsync(string messageName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var queueName = RabbitMqPhysicalAddress.Queue(MessageLane.Queue, messageName, messageName);
        var entry = _publishQueues.GetOrAdd(
            queueName,
            static (queue, state) =>
                new Lazy<Task>(() => state.Pool._DeclarePublishQueueAsync(queue, state.MessageName)),
            (Pool: this, MessageName: messageName)
        );

        return entry.Value.IsCompletedSuccessfully
            ? Task.CompletedTask
            : _AwaitPublishQueueAsync(queueName, entry, cancellationToken);
    }

    public void ForgetQueueForPublish(string messageName)
    {
        _publishQueues.TryRemove(RabbitMqPhysicalAddress.Queue(MessageLane.Queue, messageName, messageName), out _);
    }

    private async Task _AwaitPublishQueueAsync(string queueName, Lazy<Task> entry, CancellationToken cancellationToken)
    {
        try
        {
            await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (entry.Value.IsFaulted || entry.Value.IsCanceled)
        {
            // Only this entry: a concurrent caller may already have replaced it with a fresh attempt.
            _publishQueues.TryRemove(KeyValuePair.Create(queueName, entry));
            throw;
        }
    }

    // The declare is shared by every publish waiting on it, so no single caller's token cancels it; the client's own
    // continuation timeout bounds each broker call. It runs on a channel of its own because a refused declare closes
    // the channel, and a pooled publish channel must not be the one that dies.
    private async Task _DeclarePublishQueueAsync(string queueName, string messageName)
    {
        var connection = await GetConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        var channel = await connection
            .CreateChannelAsync(cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
        await using (channel.ConfigureAwait(false))
        {
            var laneExchange = RabbitMqPhysicalAddress.Exchange(Exchange, MessageLane.Queue);

            await RabbitMqQueueTopology
                .DeclareExchangeAsync(
                    channel,
                    _options,
                    laneExchange,
                    RabbitMqPhysicalAddress.ExchangeType(MessageLane.Queue),
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            await RabbitMqQueueTopology
                .DeclareQueueAsync(
                    channel,
                    _options,
                    laneExchange,
                    queueName,
                    RabbitMqPhysicalAddress.RoutingKey(MessageLane.Queue, messageName),
                    CancellationToken.None
                )
                .ConfigureAwait(false);
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

    private static Func<CancellationToken, Task<IConnection>> _CreateConnection(
        RabbitMqMessagingOptions options,
        bool automaticRecovery
    )
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
            _ApplyRecovery(factory, automaticRecovery);
            var endpoints = AmqpTcpEndpoint.ParseMultiple(options.HostName);
            foreach (var endpoint in endpoints)
            {
                endpoint.Ssl = factory.Ssl;
            }

            return cancellationToken => factory.CreateConnectionAsync(endpoints, cancellationToken);
        }

        factory.HostName = options.HostName;
        options.ConnectionFactoryOptions?.Invoke(factory);
        _ApplyRecovery(factory, automaticRecovery);
        return cancellationToken => factory.CreateConnectionAsync(cancellationToken);
    }

    // Applied after ConnectionFactoryOptions: a caller's recovery settings hold for the shared connection, but a
    // non-recovering connection must not recover whatever the callback set.
    private static void _ApplyRecovery(ConnectionFactory factory, bool automaticRecovery)
    {
        if (!automaticRecovery)
        {
            factory.AutomaticRecoveryEnabled = false;
            factory.TopologyRecoveryEnabled = false;
        }
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
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            model = await connection
                .CreateChannelAsync(BuildChannelOptions(_isPublishConfirms), cancellationToken)
                .ConfigureAwait(false);
            await _DeclareLaneExchangesAsync(model, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.ChannelModelCreateFailed(e);
            throw;
        }

        return model;
    }

    private async Task _DeclareLaneExchangesAsync(IChannel channel, CancellationToken cancellationToken)
    {
        foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
        {
            await RabbitMqQueueTopology
                .DeclareExchangeAsync(
                    channel,
                    _options,
                    RabbitMqPhysicalAddress.Exchange(Exchange, lane),
                    RabbitMqPhysicalAddress.ExchangeType(lane),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    internal static CreateChannelOptions BuildChannelOptions(bool publishConfirms)
    {
        return new(
            publisherConfirmationsEnabled: publishConfirms,
            publisherConfirmationTrackingEnabled: publishConfirms
        );
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
