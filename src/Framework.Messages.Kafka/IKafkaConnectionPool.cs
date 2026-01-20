// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

public interface IKafkaConnectionPool
{
    string ServersAddress { get; }

    IProducer<string, byte[]> RentProducer();

    bool Return(IProducer<string, byte[]> producer);
}

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
        logger.LogDebug("Kafka servers for messaging: {Servers}", _options.Servers);
    }

    public string ServersAddress => _options.Servers;

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

        producer = _BuildProducer(config);

        return producer;
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
