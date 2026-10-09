// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Confluent.Kafka;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Kafka;

/// <summary>Owns the one Kafka producer every publish in the process shares.</summary>
/// <remarks>
/// <c>IProducer</c> is thread-safe and batches concurrent sends internally, so one instance serves every publisher;
/// more instances only add broker connections and split the batches.
/// </remarks>
internal interface IKafkaProducerProvider
{
    /// <summary>Gets the formatted broker addresses the producer connects to.</summary>
    string ServersAddress { get; }

    /// <summary>Returns the shared producer, building it on first use.</summary>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    IProducer<string, byte[]> GetProducer();

    /// <summary>
    /// Discards <paramref name="producer"/> after a fatal error, so the next <see cref="GetProducer"/> builds a new one.
    /// Does nothing when another caller already replaced it.
    /// </summary>
    /// <param name="producer">The producer that raised the fatal error.</param>
    void DiscardFailedProducer(IProducer<string, byte[]> producer);
}

/// <summary>Default implementation of <see cref="IKafkaProducerProvider"/>.</summary>
internal sealed class KafkaProducerProvider : IKafkaProducerProvider, IDisposable
{
    private const int _DefaultMessageTimeoutMs = 5000;

    private readonly Lock _lock = new();
    private readonly KafkaMessagingOptions _options;
    private readonly ILogger _logger;
    private readonly Func<ProducerConfig, IProducer<string, byte[]>> _producerFactory;
    private IProducer<string, byte[]>? _producer;
    private TimeSpan _flushTimeout;
    private bool _disposed;

    public KafkaProducerProvider(
        ILogger<KafkaProducerProvider> logger,
        IOptions<KafkaMessagingOptions> options,
        Func<ProducerConfig, IProducer<string, byte[]>>? producerFactory = null
    )
    {
        _options = options.Value;
        _logger = logger;
        _producerFactory = producerFactory ?? (static config => new ProducerBuilder<string, byte[]>(config).Build());

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogKafkaServersConfigured(BrokerAddressDisplay.FormatMany(_options.Servers));
        }
    }

    public string ServersAddress => BrokerAddressDisplay.FormatMany(_options.Servers);

    public IProducer<string, byte[]> GetProducer()
    {
        lock (_lock)
        {
            Ensure.NotDisposed(_disposed, this);

            // Built under the lock rather than through Lazy<T>: a failed build is not cached, so the next publish
            // retries it.
            if (_producer is null)
            {
                var config = BuildConfig(_options);
                _flushTimeout = TimeSpan.FromMilliseconds(config.MessageTimeoutMs ?? _DefaultMessageTimeoutMs);
                _producer = _producerFactory(config);
            }

            return _producer;
        }
    }

    /// <summary>Builds the producer configuration from <paramref name="options"/>.</summary>
    internal static ProducerConfig BuildConfig(KafkaMessagingOptions options)
    {
        var config = new ProducerConfig(new Dictionary<string, string>(options.MainConfig, StringComparer.Ordinal))
        {
            BootstrapServers = options.Servers,
        };

        // A retried produce request must not write a second copy of the record.
        if (config.EnableIdempotence is null && _AllowsIdempotence(options.MainConfig))
        {
            config.EnableIdempotence = true;
        }

        config.MessageTimeoutMs ??= _DefaultMessageTimeoutMs;
        config.RequestTimeoutMs ??= 3000;

        return config;
    }

    // librdkafka refuses to build an idempotent producer with acks other than all, more than five in-flight requests,
    // no retries, or a non-FIFO queue, so a MainConfig that sets any of them keeps the non-idempotent producer it
    // asked for instead of failing every publish. Each setting is read under every name librdkafka accepts for it;
    // ProducerConfig's typed properties read only one.
    private static bool _AllowsIdempotence(Dictionary<string, string> mainConfig)
    {
        return _Get(mainConfig, "acks", "request.required.acks") is null or "all" or "-1"
            && _GetInt(mainConfig, "max.in.flight.requests.per.connection", "max.in.flight") is null or <= 5
            && _GetInt(mainConfig, "retries", "message.send.max.retries") is null or > 0
            && _Get(mainConfig, "queuing.strategy") is null or "fifo";
    }

    private static string? _Get(Dictionary<string, string> mainConfig, params ReadOnlySpan<string> keys)
    {
        foreach (var key in keys)
        {
            if (mainConfig.TryGetValue(key, out var value))
            {
                return value.Trim().ToLowerInvariant();
            }
        }

        return null;
    }

    private static int? _GetInt(Dictionary<string, string> mainConfig, params ReadOnlySpan<string> keys)
    {
        return int.TryParse(_Get(mainConfig, keys), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public void DiscardFailedProducer(IProducer<string, byte[]> producer)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_producer, producer))
            {
                return;
            }

            _producer = null;
        }

        _logger.LogKafkaProducerDiscarded();

        // A fatal error has already failed every queued record, so there is nothing left to flush.
        producer.Dispose();
    }

    public void Dispose()
    {
        IProducer<string, byte[]>? producer;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            producer = _producer;
            _producer = null;
        }

        if (producer is null)
        {
            return;
        }

        try
        {
            // Every queued record is delivered or failed within message.timeout.ms, so a longer flush cannot help.
            var pending = producer.Flush(_flushTimeout);

            if (pending > 0)
            {
                _logger.LogKafkaProducerFlushIncomplete(pending);
            }
        }
        catch (KafkaException e)
        {
            _logger.LogKafkaProducerFlushFailed(e);
        }
        finally
        {
            producer.Dispose();
        }
    }
}

internal static partial class KafkaProducerProviderLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "KafkaServersConfigured",
        Level = LogLevel.Debug,
        Message = "Kafka servers for messaging: {Servers}"
    )]
    public static partial void LogKafkaServersConfigured(this ILogger logger, string servers);

    [LoggerMessage(
        EventId = 2,
        EventName = "KafkaProducerFlushIncomplete",
        Level = LogLevel.Warning,
        Message = "Kafka producer disposed with {Pending} record(s) still undelivered after the flush timeout"
    )]
    public static partial void LogKafkaProducerFlushIncomplete(this ILogger logger, int pending);

    [LoggerMessage(
        EventId = 3,
        EventName = "KafkaProducerFlushFailed",
        Level = LogLevel.Warning,
        Message = "Kafka producer flush failed during disposal"
    )]
    public static partial void LogKafkaProducerFlushFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4,
        EventName = "KafkaProducerDiscarded",
        Level = LogLevel.Warning,
        Message = "Kafka producer raised a fatal error; it was discarded and the next publish builds a new one"
    )]
    public static partial void LogKafkaProducerDiscarded(this ILogger logger);
}
