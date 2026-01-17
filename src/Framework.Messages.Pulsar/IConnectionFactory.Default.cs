// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Pulsar.Client.Api;

namespace Framework.Messages;

public class ConnectionFactory : IConnectionFactory, IAsyncDisposable
{
    private readonly Lock _lock = new();
    private PulsarClient? _client;
    private readonly PulsarOptions _options;
    private readonly ConcurrentDictionary<string, Task<IProducer<byte[]>>> _topicProducers;

    public ConnectionFactory(ILogger<ConnectionFactory> logger, IOptions<PulsarOptions> options)
    {
        _options = options.Value;
        _topicProducers = new ConcurrentDictionary<string, Task<IProducer<byte[]>>>(StringComparer.Ordinal);

        logger.LogDebug(
            "CAP Pulsar configuration: {Configuration}",
            JsonConvert.SerializeObject(_options, Formatting.Indented)
        );
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var value in _topicProducers.Values)
        {
            _ = (await value).DisposeAsync();
        }
    }

    public string ServersAddress => _options.ServiceUrl;

    public async Task<IProducer<byte[]>> CreateProducerAsync(string topic)
    {
        _client ??= RentClient();

        async Task<IProducer<byte[]>> valueFactory(string top)
        {
            return await _client.NewProducer().Topic(top).CreateAsync();
        }

        //connection may lost
        return await _topicProducers.GetOrAdd(topic, valueFactory);
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
}
