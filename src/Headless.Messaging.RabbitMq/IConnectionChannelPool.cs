// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Headless.Messaging.RabbitMq;

public interface IConnectionChannelPool
{
    string HostAddress { get; }

    string Exchange { get; }

    Task<IConnection> GetConnectionAsync();

    Task<IChannel> Rent();

    bool Return(IChannel context);
}

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

        _logger.LogDebug(
            "RabbitMQ configuration:'HostName:{OptionsHostName}, Port:{OptionsPort}, UserName:{OptionsUserName}, VirtualHost:{OptionsVirtualHost}, ExchangeName:{OptionsExchangeName}'",
            options.HostName,
            options.Port,
            options.UserName,
            options.VirtualHost,
            options.ExchangeName
        );
    }

    async Task<IChannel> IConnectionChannelPool.Rent()
    {
        await _poolSemaphore.WaitAsync().AnyContext();

        try
        {
            return await Rent().AnyContext();
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

        await _connectionLock.WaitAsync().AnyContext();
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection?.Dispose();
            _connection = await _connectionActivator().AnyContext();
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
            await channel.DisposeAsync().AnyContext();
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync().AnyContext();
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

    public async Task<IChannel> Rent()
    {
        if (_pool.TryDequeue(out var model))
        {
            Interlocked.Decrement(ref _count);

            Debug.Assert(_count >= 0);

            return model;
        }

        try
        {
            var connection = await GetConnectionAsync().AnyContext();
            model = await connection.CreateChannelAsync(new CreateChannelOptions(_isPublishConfirms, false));
            await model.ExchangeDeclareAsync(Exchange, RabbitMqOptions.ExchangeType, true);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "RabbitMQ channel model create failed!");
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
