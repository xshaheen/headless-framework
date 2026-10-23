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

    /// <summary>
    /// Gets the largest message body, in bytes, that a consumer on this pool's connections can receive: the connection
    /// factory's <c>MaxInboundMessageBodySize</c>, which
    /// <see cref="RabbitMqMessagingOptions.ConnectionFactoryOptions"/> can change.
    /// </summary>
    /// <remarks>
    /// The broker accepts bodies up to its own <c>max_message_size</c>, 128 MiB by default, but the client closes the
    /// whole connection when it receives a body over this limit, so a publish must not send one.
    /// </remarks>
    uint MaxMessageBodySize { get; }

    /// <summary>Returns the shared, lazily-established AMQP connection, opening it if necessary.</summary>
    /// <remarks>
    /// While RabbitMQ.Client recovers the lost shared connection, this returns that connection, and calls on it fail
    /// until the recovery completes. Replacing it would cancel the recovery of every consumer on it.
    /// </remarks>
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
    /// Proves the Queue-lane queue of <paramref name="messageName"/> exists, once per process; <c>resources provision</c>
    /// created and bound it. A failed attempt is forgotten, so the next call tries again.
    /// </summary>
    /// <remarks>
    /// A Queue-lane message goes to a direct exchange, which drops a message no queue is bound for, so a publish to a
    /// missing queue fails here, naming <c>resources provision</c>, instead of losing the message.
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
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private readonly Func<CancellationToken, Task<IConnection>> _connectionActivator;
    private readonly Func<CancellationToken, Task<IConnection>> _nonRecoveringConnectionActivator;
    private readonly bool _isPublishConfirms;
    private readonly bool _sharedConnectionRecovers;
    private readonly TimeProvider _recoveryClock;
    private readonly RabbitMqMessagingOptions _options;

    // How long a lost shared connection is left to the client's recovery before the pool replaces it. The client
    // retries until the connection is disposed, but a recovery that failed for good (a topology recovery error, a
    // close the client does not recover from) leaves the connection closed forever, and every publish would fail.
    // A broker restart recovers well inside this window; replacing the connection after it gives up on the consumers
    // the recovery would have restored, which the transport health check rebuilds.
    internal static readonly TimeSpan RecoveryWindow = TimeSpan.FromMinutes(2);

    // The timestamp, on _recoveryClock, at which the current connection was first seen lost; 0 while it is open.
    private long _lostSince;

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
        : this(
            logger,
            messagingAccessorOptionsAccessor,
            optionsAccessor,
            connectionActivator: null,
            recoveryClock: TimeProvider.System
        ) { }

    internal ConnectionChannelPool(
        ILogger<ConnectionChannelPool> logger,
        IOptions<MessagingOptions> messagingAccessorOptionsAccessor,
        IOptions<RabbitMqMessagingOptions> optionsAccessor,
        Func<CancellationToken, Task<IConnection>>? connectionActivator,
        TimeProvider? recoveryClock = null
    )
    {
        _logger = logger;
        // Real time unless a test steps it: the window bounds a broker outage, not an app schedule.
        _recoveryClock = recoveryClock ?? TimeProvider.System;
        var messagingOptions = messagingAccessorOptionsAccessor.Value;
        var options = optionsAccessor.Value;

        _maxSize = options.ChannelPoolSize;
        _pool = new ConcurrentQueue<IChannel>();
        _poolSemaphore = new SemaphoreSlim(options.ChannelPoolSize, options.ChannelPoolSize);

        if (connectionActivator is null)
        {
            var factory = _CreateFactory(options, automaticRecovery: true);
            _connectionActivator = _CreateActivator(factory, options.HostName);
            _nonRecoveringConnectionActivator = _CreateActivator(
                _CreateFactory(options, automaticRecovery: false),
                options.HostName
            );

            // Read through an endpoint, which caps the factory's value at the largest body the client accepts at all.
            MaxMessageBodySize = factory.Endpoint.MaxInboundMessageBodySize;
            _sharedConnectionRecovers = factory.AutomaticRecoveryEnabled;
        }
        else
        {
            _connectionActivator = connectionActivator;
            _nonRecoveringConnectionActivator = connectionActivator;
            MaxMessageBodySize = ConnectionFactory.DefaultMaxInboundMessageBodySize;
            _sharedConnectionRecovers = true;
        }

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

    public uint MaxMessageBodySize { get; }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { } current && _IsUsable(current))
        {
            return current;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { } connection && _IsUsable(connection))
            {
                return connection;
            }

            _connection?.Dispose();
            Interlocked.Exchange(ref _lostSince, 0);
            _connection = await _connectionActivator(cancellationToken).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    // An open connection, or a lost one the client is still recovering within RecoveryWindow.
    private bool _IsUsable(IConnection connection)
    {
        if (connection.IsOpen)
        {
            Interlocked.Exchange(ref _lostSince, 0);
            return true;
        }

        if (!_IsRecovering(connection))
        {
            return false;
        }

        var lostSince = Interlocked.Read(ref _lostSince);
        if (lostSince == 0)
        {
            Interlocked.CompareExchange(ref _lostSince, _recoveryClock.GetTimestamp(), 0);
            return true;
        }

        return _recoveryClock.GetElapsedTime(lostSince) < RecoveryWindow;
    }

    // RabbitMQ.Client recovers a connection after any loss it did not cause itself, except the broker's ACCESS_REFUSED,
    // and retries until the connection is disposed (AutorecoveringConnection, v7). Disposing a connection it is
    // recovering cancels that recovery, and with it the recovery of every channel and consumer on the connection.
    private bool _IsRecovering(IConnection connection)
    {
        return _sharedConnectionRecovers
            && connection.CloseReason is { } reason
            && reason.Initiator is not ShutdownInitiator.Application
            && !(reason.Initiator is ShutdownInitiator.Peer && reason.ReplyCode == Constants.AccessRefused);
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
            static (queue, pool) => new Lazy<Task>(() => pool._RequirePublishQueueAsync(queue)),
            this
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

    // The check is shared by every publish waiting on it, so no single caller's token cancels it; the client's own
    // continuation timeout bounds each broker call. It runs on a channel of its own because a refused check closes
    // the channel, and a pooled publish channel must not be the one that dies.
    private async Task _RequirePublishQueueAsync(string queueName)
    {
        var connection = await GetConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        var channel = await connection
            .CreateChannelAsync(cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
        await using (channel.ConfigureAwait(false))
        {
            var laneExchange = RabbitMqPhysicalAddress.Exchange(Exchange, MessageLane.Queue);

            await RabbitMqQueueTopology
                .RequireExchangeAsync(channel, laneExchange, CancellationToken.None)
                .ConfigureAwait(false);

            _ = await RabbitMqQueueTopology
                .RequireQueueAsync(channel, queueName, CancellationToken.None)
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

    private static ConnectionFactory _CreateFactory(RabbitMqMessagingOptions options, bool automaticRecovery)
    {
        var factory = new ConnectionFactory
        {
            UserName = options.UserName,
            Port = options.Port,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            ClientProvidedName = Assembly.GetEntryAssembly()?.GetName().Name!.ToLower(CultureInfo.InvariantCulture),
        };

        if (!options.HostName.Contains(',', StringComparison.Ordinal))
        {
            factory.HostName = options.HostName;
        }

        options.ConnectionFactoryOptions?.Invoke(factory);
        _ApplyRecovery(factory, automaticRecovery);
        return factory;
    }

    private static Func<CancellationToken, Task<IConnection>> _CreateActivator(
        ConnectionFactory factory,
        string hostName
    )
    {
        if (hostName.Contains(',', StringComparison.Ordinal))
        {
            // A parsed endpoint carries the client's default body limit, not the factory's, so each one is rebuilt
            // with the factory's body limit; TLS is set first, because an endpoint without a port resolves the default
            // port from it.
            var endpoints = AmqpTcpEndpoint
                .ParseMultiple(hostName)
                .Select(endpoint =>
                {
                    endpoint.Ssl = factory.Ssl;
                    return new AmqpTcpEndpoint(
                        endpoint.HostName,
                        endpoint.Port,
                        factory.Ssl,
                        factory.MaxInboundMessageBodySize
                    );
                })
                .ToArray();

            return cancellationToken => factory.CreateConnectionAsync(endpoints, cancellationToken);
        }

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
                .RequireExchangeAsync(channel, RabbitMqPhysicalAddress.Exchange(Exchange, lane), cancellationToken)
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
