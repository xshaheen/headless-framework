// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Manages the shared Pulsar client and per-topic producer cache for the Pulsar transport.
/// </summary>
/// <remarks>
/// The <c>PulsarClient</c> is created lazily on the first call to <see cref="RentClientAsync"/> or
/// <see cref="CreateProducerAsync"/>. Producers are cached per topic; a failed producer task is
/// evicted from the cache so the next call creates a fresh producer.
/// </remarks>
public interface IConnectionFactory
{
    /// <summary>Gets the formatted Pulsar service URL used by this factory.</summary>
    string ServersAddress { get; }

    /// <summary>
    /// Returns a cached producer for <paramref name="topic"/>, creating one on first call.
    /// A failed producer is evicted from the cache so the next call can retry.
    /// </summary>
    /// <param name="topic">The fully-qualified Pulsar topic name.</param>
    Task<IProducer<byte[]>> CreateProducerAsync(string topic);

    /// <summary>
    /// Returns the shared <c>PulsarClient</c>, creating it if it has not been opened yet.
    /// The client is long-lived; do not dispose it directly.
    /// </summary>
    Task<PulsarClient> RentClientAsync();
}

/// <summary>Default implementation of <see cref="IConnectionFactory"/>.</summary>
public sealed class ConnectionFactory : IConnectionFactory, IAsyncDisposable
{
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private PulsarClient? _client;
    private readonly MessagingPulsarOptions _options;
    private readonly Func<string, Task<IProducer<byte[]>>>? _producerFactoryOverride;
    private readonly ConcurrentDictionary<string, Task<IProducer<byte[]>>> _topicProducers;

    public ConnectionFactory(
        ILogger<ConnectionFactory> logger,
        IOptions<MessagingPulsarOptions> options,
        Func<string, Task<IProducer<byte[]>>>? producerFactoryOverride = null
    )
    {
        _options = options.Value;
        _producerFactoryOverride = producerFactoryOverride;
        _topicProducers = new ConcurrentDictionary<string, Task<IProducer<byte[]>>>(StringComparer.Ordinal);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogPulsarConfiguration(
                BrokerAddressDisplay.Format(_options.ServiceUrl),
                _options.EnableClientLog,
                _options.TlsOptions is not null
            );
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var value in _topicProducers.Values)
        {
            await (await value.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        }

        if (_client is not null)
        {
            await _client.CloseAsync().ConfigureAwait(false);
        }

        _clientLock.Dispose();
    }

    public string ServersAddress => BrokerAddressDisplay.Format(_options.ServiceUrl);

    public async Task<IProducer<byte[]>> CreateProducerAsync(string topic)
    {
        if (_producerFactoryOverride is null)
        {
            _client ??= await RentClientAsync().ConfigureAwait(false);
        }

        var producerTask = _topicProducers.GetOrAdd(
            topic,
            static (top, state) => state._CreateProducerAsync(top),
            this
        );

        try
        {
            return await producerTask.ConfigureAwait(false);
        }
        catch
        {
            _topicProducers.TryRemove(new KeyValuePair<string, Task<IProducer<byte[]>>>(topic, producerTask));
            throw;
        }
    }

    public async Task<PulsarClient> RentClientAsync()
    {
        // Serialize lazy creation through an async lock (the client is long-lived, so this path is cold).
        await _clientLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is null)
            {
                var builder = new PulsarClientBuilder().ServiceUrl(_options.ServiceUrl);
                if (_options.TlsOptions != null)
                {
                    builder.EnableTlsHostnameVerification(_options.TlsOptions.TlsHostnameVerificationEnable);
                    builder.AllowTlsInsecureConnection(_options.TlsOptions.TlsAllowInsecureConnection);
                    builder.TlsTrustCertificate(_options.TlsOptions.TlsTrustCertificate);
                    builder.Authentication(_options.TlsOptions.Authentication);
                    builder.TlsProtocols(_options.TlsOptions.TlsProtocols);
                }

                _client = await builder.BuildAsync().ConfigureAwait(false);
            }

            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private Task<IProducer<byte[]>> _CreateProducerAsync(string topic)
    {
        if (_producerFactoryOverride is not null)
        {
            return _producerFactoryOverride(topic);
        }

        return _client!.NewProducer().Topic(topic).CreateAsync();
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
    public static partial void LogPulsarConfiguration(
        this ILogger logger,
        string serviceUrl,
        bool enableClientLog,
        bool hasTlsOptions
    );
}
