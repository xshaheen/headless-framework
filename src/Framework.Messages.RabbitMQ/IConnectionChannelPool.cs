// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Framework.Messages.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Framework.Messages;

public interface IConnectionChannelPool
{
    string HostAddress { get; }

    string Exchange { get; }

    IConnection GetConnection();

    Task<IChannel> Rent();

    bool Return(IChannel context);
}

public sealed class ConnectionChannelPool : IConnectionChannelPool, IDisposable
{
    private const int _DefaultPoolSize = 15;
    private static readonly Lock _Lock = new();

    private readonly Func<Task<IConnection>> _connectionActivator;
    private readonly bool _isPublishConfirms;
    private readonly ILogger<ConnectionChannelPool> _logger;
    private readonly ConcurrentQueue<IChannel> _pool;
    private IConnection? _connection;

    private int _count;
    private int _maxSize;

    public ConnectionChannelPool(
        ILogger<ConnectionChannelPool> logger,
        IOptions<MessagingOptions> capOptionsAccessor,
        IOptions<RabbitMqOptions> optionsAccessor
    )
    {
        _logger = logger;
        _maxSize = _DefaultPoolSize;
        _pool = new ConcurrentQueue<IChannel>();

        var capOptions = capOptionsAccessor.Value;
        var options = optionsAccessor.Value;

        _connectionActivator = _CreateConnection(options);
        _isPublishConfirms = options.PublishConfirms;

        HostAddress = $"{options.HostName}:{options.Port}";
        Exchange = string.Equals("v1", capOptions.Version, StringComparison.Ordinal)
            ? options.ExchangeName
            : $"{options.ExchangeName}.{capOptions.Version}";

        _logger.LogDebug(
            "RabbitMQ configuration:'HostName:{OptionsHostName}, Port:{OptionsPort}, UserName:{OptionsUserName}, VirtualHost:{OptionsVirtualHost}, ExchangeName:{OptionsExchangeName}'",
            options.HostName,
            options.Port,
            options.UserName,
            options.VirtualHost,
            options.ExchangeName
        );
    }

    Task<IChannel> IConnectionChannelPool.Rent()
    {
        lock (_Lock)
        {
            while (_count > _maxSize)
            {
                Thread.SpinWait(1);
            }

            return Rent();
        }
    }

    bool IConnectionChannelPool.Return(IChannel connection)
    {
        return Return(connection);
    }

    public string HostAddress { get; }

    public string Exchange { get; }

    public IConnection GetConnection()
    {
        lock (_Lock)
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection?.Dispose();
            _connection = _connectionActivator().GetAwaiter().GetResult();
            return _connection;
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
            model = await GetConnection().CreateChannelAsync(new CreateChannelOptions(_isPublishConfirms, false));
            await model.ExchangeDeclareAsync(Exchange, RabbitMqOptions.ExchangeType, true);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "RabbitMQ channel model create failed!");
            Console.WriteLine(e);
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
