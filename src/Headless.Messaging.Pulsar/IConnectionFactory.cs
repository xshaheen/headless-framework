// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;

namespace Headless.Messaging.Pulsar;

public interface IConnectionFactory
{
    string ServersAddress { get; }

    Task<IProducer<byte[]>> CreateProducerAsync(string topic);

    PulsarClient RentClient();
}

public sealed class ConnectionFactory : IConnectionFactory, IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly ILogger<ConnectionFactory> _logger;
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
        _logger = logger;
        _options = options.Value;
        _producerFactoryOverride = producerFactoryOverride;
        _topicProducers = new ConcurrentDictionary<string, Task<IProducer<byte[]>>>(StringComparer.Ordinal);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Messaging Pulsar configuration: ServiceUrl={ServiceUrl}, EnableClientLog={EnableClientLog}, HasTlsOptions={HasTlsOptions}",
                _options.ServiceUrl,
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
    }

    public string ServersAddress => _options.ServiceUrl;

    public async Task<IProducer<byte[]>> CreateProducerAsync(string topic)
    {
        if (_producerFactoryOverride is null)
        {
            _client ??= RentClient();
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

    public PulsarClient RentClient()
    {
        lock (_lock)
        {
            if (_client is null)
            {
                var builder = new PulsarClientBuilder().ServiceUrl(_options.ServiceUrl);
                if (_options.TlsOptions != null)
                {
                    builder.EnableTls(_options.TlsOptions.UseTls);
                    builder.EnableTlsHostnameVerification(_options.TlsOptions.TlsHostnameVerificationEnable);
                    builder.AllowTlsInsecureConnection(_options.TlsOptions.TlsAllowInsecureConnection);
                    builder.TlsTrustCertificate(_options.TlsOptions.TlsTrustCertificate);
                    builder.Authentication(_options.TlsOptions.Authentication);
                    builder.TlsProtocols(_options.TlsOptions.TlsProtocols);
                }

                _client = builder.BuildAsync().Result;
            }

            return _client;
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
