// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;

namespace Headless.Messaging.Pulsar;

/// <summary>Default implementation of <see cref="IConnectionFactory"/>.</summary>
internal sealed class ConnectionFactory : IConnectionFactory, IAsyncDisposable
{
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private PulsarClient? _client;
    private Task<PulsarClient>? _clientBuildTask;
    private int _disposed;
    private readonly PulsarMessagingOptions _options;
    private readonly Func<string, Task<IProducer<byte[]>>>? _producerFactoryOverride;
    private readonly ConcurrentDictionary<string, Task<IProducer<byte[]>>> _topicProducers;

    public ConnectionFactory(
        ILogger<ConnectionFactory> logger,
        IOptions<PulsarMessagingOptions> options,
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var value in _topicProducers.Values)
        {
            await (await value.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        }

        PulsarClient? client;
        Task<PulsarClient>? clientBuildTask;

        await _clientLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            client = _client;
            clientBuildTask = _clientBuildTask;
        }
        finally
        {
            _clientLock.Release();
        }

        if (client is not null)
        {
            await client.CloseAsync().ConfigureAwait(false);
        }
        else if (clientBuildTask is not null)
        {
            _CloseWhenCompletedAsync(clientBuildTask).Forget();
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

    public async Task<PulsarClient> RentClientAsync(CancellationToken cancellationToken = default)
    {
        Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

        Task<PulsarClient> clientBuildTask;

        await _clientLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

            if (_client is not null)
            {
                return _client;
            }

            _clientBuildTask ??= _BuildClientAsync();
            clientBuildTask = _clientBuildTask;
        }
        finally
        {
            _clientLock.Release();
        }

        PulsarClient client;
        try
        {
            client = await clientBuildTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (clientBuildTask.IsFaulted || clientBuildTask.IsCanceled)
        {
            await _clientLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_clientBuildTask, clientBuildTask))
                {
                    _clientBuildTask = null;
                }
            }
            finally
            {
                _clientLock.Release();
            }

            throw;
        }

        await _clientLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);
            return _client ??= client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private async Task<PulsarClient> _BuildClientAsync()
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

        return await builder.BuildAsync().ConfigureAwait(false);
    }

    private static async Task _CloseWhenCompletedAsync(Task<PulsarClient> clientTask)
    {
        try
        {
            var client = await clientTask.ConfigureAwait(false);
            await client.CloseAsync().ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Best-effort cleanup for an SDK operation abandoned by caller cancellation.
        catch
        {
            // ignored
        }
#pragma warning restore ERP022
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
