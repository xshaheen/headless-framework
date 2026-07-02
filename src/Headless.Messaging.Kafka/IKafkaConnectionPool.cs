// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Confluent.Kafka;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Kafka;

/// <summary>
/// Manages a pool of Kafka producers for publish operations.
/// </summary>
/// <remarks>
/// Producers are not thread-safe for concurrent sends; renting a producer from the pool ensures
/// exclusive access. Call <see cref="Return"/> after each publish to recycle the producer.
/// When the pool is at capacity, <see cref="Return"/> disposes the surplus producer instead.
/// </remarks>
public interface IKafkaConnectionPool
{
    /// <summary>Gets the formatted broker addresses used by this pool.</summary>
    string ServersAddress { get; }

    /// <summary>
    /// Takes a producer from the pool, or creates a new one when the pool is empty.
    /// The caller must return the producer via <see cref="Return"/> when the publish is complete.
    /// </summary>
    IProducer<string, byte[]> RentProducer();

    /// <summary>
    /// Returns a producer to the pool. If the pool is already at its maximum size,
    /// the producer is disposed instead.
    /// </summary>
    /// <param name="producer">The producer to return or dispose.</param>
    /// <returns>
    /// <see langword="true"/> if the producer was returned to the pool;
    /// <see langword="false"/> if it was disposed because the pool was full.
    /// </returns>
    bool Return(IProducer<string, byte[]> producer);
}

/// <summary>Default implementation of <see cref="IKafkaConnectionPool"/>.</summary>
public sealed class KafkaConnectionPool : IKafkaConnectionPool, IDisposable
{
    private readonly MessagingKafkaOptions _options;
    private readonly ConcurrentQueue<IProducer<string, byte[]>> _producerPool;
    private int _maxSize;
    private int _pCount;

    public KafkaConnectionPool(ILogger<KafkaConnectionPool> logger, IOptions<MessagingKafkaOptions> options)
    {
        _options = options.Value;
        _producerPool = new();
        _maxSize = _options.ConnectionPoolSize;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogKafkaServersConfigured(BrokerAddressDisplay.FormatMany(_options.Servers));
        }
    }

    public string ServersAddress => BrokerAddressDisplay.FormatMany(_options.Servers);

    public IProducer<string, byte[]> RentProducer()
    {
        if (_producerPool.TryDequeue(out var producer))
        {
            Interlocked.Decrement(ref _pCount);

            return producer;
        }

        var config = new ProducerConfig(new Dictionary<string, string>(_options.MainConfig, StringComparer.Ordinal))
        {
            BootstrapServers = _options.Servers,
        };

        config.QueueBufferingMaxMessages ??= 10;
        config.MessageTimeoutMs ??= 5000;
        config.RequestTimeoutMs ??= 3000;

        return _BuildProducer(config);
    }

    public bool Return(IProducer<string, byte[]> producer)
    {
        if (Interlocked.Increment(ref _pCount) <= _maxSize)
        {
            _producerPool.Enqueue(producer);

            return true;
        }

        producer.Dispose();

        Interlocked.Decrement(ref _pCount);

        return false;
    }

    public void Dispose()
    {
        _maxSize = 0;

        while (_producerPool.TryDequeue(out var context))
        {
            context.Dispose();
        }
    }

    private static IProducer<string, byte[]> _BuildProducer(ProducerConfig config)
    {
        return new ProducerBuilder<string, byte[]>(config).Build();
    }
}

internal static partial class KafkaConnectionPoolLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "KafkaServersConfigured",
        Level = LogLevel.Debug,
        Message = "Kafka servers for messaging: {Servers}"
    )]
    public static partial void LogKafkaServersConfigured(this ILogger logger, string servers);
}
