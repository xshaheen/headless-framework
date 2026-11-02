// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections.Concurrent;
using DotPulsar;
using DotPulsar.Abstractions;
using DotPulsar.Exceptions;
using DotPulsar.Extensions;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Manages the shared Pulsar client and per-topic producer cache for the Pulsar transport.
/// </summary>
/// <remarks>
/// The client is created on first use and connects lazily, so creating it touches no broker. Producers are cached per
/// topic; a producer that reached a final state (faulted, closed, or fenced) is replaced on the next call, so a
/// publish after such a failure gets a fresh producer.
/// </remarks>
internal interface IConnectionFactory
{
    /// <summary>Gets the formatted Pulsar service URL used by this factory.</summary>
    string ServersAddress { get; }

    /// <summary>Gets how long a send may wait for the broker; <see cref="TimeSpan.Zero"/> waits indefinitely.</summary>
    TimeSpan SendTimeout { get; }

    /// <summary>Gets the messages the client refused as too large, which tell a size refusal from other faults.</summary>
    PulsarSizeRefusals SizeRefusals { get; }

    /// <summary>
    /// Returns a cached producer for <paramref name="topic"/>, creating one on the first call and after the cached one
    /// reached a final state.
    /// </summary>
    /// <param name="topic">The Pulsar topic name.</param>
    IProducer<ReadOnlySequence<byte>> GetProducer(string topic);

    /// <summary>Returns the shared Pulsar client, creating it on first use. The client is long-lived; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The factory was disposed.</exception>
    IPulsarClient RentClient();
}

/// <summary>Default implementation of <see cref="IConnectionFactory"/>.</summary>
internal sealed class ConnectionFactory : IConnectionFactory, IAsyncDisposable
{
    private readonly Lock _clientLock = new();
    private readonly ILogger _logger;
    private readonly PulsarMessagingOptions _options;
    private readonly Func<string, IProducer<ReadOnlySequence<byte>>>? _producerFactoryOverride;
    private readonly ConcurrentDictionary<string, IProducer<ReadOnlySequence<byte>>> _topicProducers;
    private IPulsarClient? _client;
    private int _disposed;

    public ConnectionFactory(
        ILogger<ConnectionFactory> logger,
        IOptions<PulsarMessagingOptions> options,
        Func<string, IProducer<ReadOnlySequence<byte>>>? producerFactoryOverride = null
    )
    {
        _logger = logger;
        _options = options.Value;
        _producerFactoryOverride = producerFactoryOverride;
        _topicProducers = new ConcurrentDictionary<string, IProducer<ReadOnlySequence<byte>>>(StringComparer.Ordinal);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogPulsarConfiguration(
                BrokerAddressDisplay.Format(_options.ServiceUrl),
                _options.EnableClientLog,
                _options.TlsOptions is not null
            );
        }
    }

    public string ServersAddress => BrokerAddressDisplay.Format(_options.ServiceUrl);

    public TimeSpan SendTimeout => _options.Producer.SendTimeout;

    public PulsarSizeRefusals SizeRefusals { get; } = new(TimeProvider.System);

    public IProducer<ReadOnlySequence<byte>> GetProducer(string topic)
    {
        Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

        while (true)
        {
            var producer = _topicProducers.GetOrAdd(topic, static (top, state) => state._CreateProducer(top), this);

            if (!producer.State.IsFinalState())
            {
                return producer;
            }

            // A faulted producer never recovers on its own; replace it so this publish, and every later one, retries.
            if (_topicProducers.TryRemove(new KeyValuePair<string, IProducer<ReadOnlySequence<byte>>>(topic, producer)))
            {
                _DisposeInBackgroundAsync(producer).Forget();
            }
        }
    }

    public IPulsarClient RentClient()
    {
        Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

        lock (_clientLock)
        {
            Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);
            return _client ??= _BuildClient();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var producer in _topicProducers.Values)
        {
            await producer.DisposeAsync().ConfigureAwait(false);
        }

        _topicProducers.Clear();

        IPulsarClient? client;
        lock (_clientLock)
        {
            client = _client;
            _client = null;
        }

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private IPulsarClient _BuildClient()
    {
        var builder = PulsarClient.Builder().ServiceUrl(new Uri(_options.ServiceUrl, UriKind.Absolute));

        if (_options.Authentication is { } authentication)
        {
            builder.Authentication(authentication);
        }

        if (_options.TlsOptions is { } tls)
        {
            builder
                .VerifyCertificateAuthority(tls.VerifyCertificateAuthority)
                .VerifyCertificateName(tls.VerifyCertificateName)
                .CheckCertificateRevocation(tls.CheckCertificateRevocation);

            if (tls.TrustedCertificateAuthority is { } authority)
            {
                builder.TrustedCertificateAuthority(authority);
            }

            if (tls.ClientCertificate is { } certificate)
            {
                builder.AuthenticateUsingClientCertificate(certificate);
            }
        }

        // The handler only observes: it leaves the exception unhandled, so the client's default policy still decides
        // whether to retry or fault. It is the only place DotPulsar reports why it faulted a producer on a large message.
        builder.ExceptionHandler(context =>
        {
            if (context.Exception is TooLargeMessageException tooLarge)
            {
                SizeRefusals.Record(tooLarge);
            }

            if (_options.EnableClientLog)
            {
                _logger.LogPulsarClientException(context.Exception);
            }
        });

        return builder.Build();
    }

    private IProducer<ReadOnlySequence<byte>> _CreateProducer(string topic)
    {
        if (_producerFactoryOverride is not null)
        {
            return _producerFactoryOverride(topic);
        }

        var builder = RentClient().NewProducer().Topic(topic).CompressionType(_options.Producer.CompressionType);

        if (_options.EnableClientLog)
        {
            builder.StateChangedHandler(changed =>
                _logger.LogPulsarProducerState(changed.Producer.Topic, changed.ProducerState)
            );
        }

        return builder.Create();
    }

    private static async Task _DisposeInBackgroundAsync(IAsyncDisposable producer)
    {
        try
        {
            await producer.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Best-effort cleanup of a producer that already faulted; nothing else owns it.
        catch
        {
            // ignored
        }
#pragma warning restore ERP022
    }
}

internal static partial class ConnectionFactoryLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "PulsarConfiguration",
        Level = LogLevel.Debug,
        Message = "Messaging Pulsar configuration: ServiceUrl={ServiceUrl}, EnableClientLog={EnableClientLog}, HasTlsOptions={HasTlsOptions}"
    )]
    internal static partial void LogPulsarConfiguration(
        this ILogger logger,
        string serviceUrl,
        bool enableClientLog,
        bool hasTlsOptions
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "PulsarClientException",
        Level = LogLevel.Information,
        Message = "Pulsar client handled an exception; the client retries or faults per its policy"
    )]
    internal static partial void LogPulsarClientException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3,
        EventName = "PulsarProducerState",
        Level = LogLevel.Information,
        Message = "Pulsar producer for topic {Topic} is now {State}"
    )]
    internal static partial void LogPulsarProducerState(this ILogger logger, string topic, ProducerState state);
}
