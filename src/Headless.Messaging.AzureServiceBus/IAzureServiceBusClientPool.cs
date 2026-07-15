// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Headless.Messaging.AzureServiceBus.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>
/// Owns the single <see cref="ServiceBusClient"/> for the configured namespace and the pool of
/// <see cref="ServiceBusSender"/> instances keyed by entity path (topic or queue name).
/// </summary>
/// <remarks>
/// One <see cref="ServiceBusClient"/> equals one multiplexed AMQP connection; senders created from it
/// share that connection. The pool is registered as a singleton and shared by the bus and queue
/// transports so co-registering both uses a single connection and a single sender per destination.
/// Disposal drains every materialized sender before disposing the client.
/// </remarks>
internal interface IAzureServiceBusClientPool : IAsyncDisposable
{
    /// <summary>
    /// Gets the shared <see cref="ServiceBusSender"/> for the given entity path, creating the
    /// namespace client and the sender on first use. The returned sender is long-lived and shared;
    /// do not dispose it.
    /// </summary>
    /// <param name="entityPath">The topic or queue name the sender publishes to.</param>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    ServiceBusSender GetSender(string entityPath);
}

/// <summary>Default implementation of <see cref="IAzureServiceBusClientPool"/>.</summary>
/// <remarks>
/// Internal implementation detail: consumers resolve <see cref="IAzureServiceBusClientPool"/> from DI and
/// never reference this concrete type. Kept <see langword="internal"/> to stay off the package's public surface.
/// </remarks>
internal sealed class AzureServiceBusClientPool : IAzureServiceBusClientPool
{
    private readonly ILogger _logger;
    private readonly IOptions<AzureServiceBusMessagingOptions> _options;
    private readonly Func<AzureServiceBusMessagingOptions, ServiceBusClient> _clientFactory;
    private readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _senders = new(StringComparer.Ordinal);
    private readonly Lock _clientLock = new();
    private ServiceBusClient? _client;
    private int _disposed;

    public AzureServiceBusClientPool(
        ILogger<AzureServiceBusClientPool> logger,
        IOptions<AzureServiceBusMessagingOptions> options
    )
        : this(logger, options, ServiceBusHelpers.CreateClient) { }

    /// <summary>Test seam: lets unit tests substitute the client and count creations.</summary>
    internal AzureServiceBusClientPool(
        ILogger<AzureServiceBusClientPool> logger,
        IOptions<AzureServiceBusMessagingOptions> options,
        Func<AzureServiceBusMessagingOptions, ServiceBusClient> clientFactory
    )
    {
        _logger = logger;
        _options = options;
        _clientFactory = clientFactory;
    }

    public ServiceBusSender GetSender(string entityPath)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var lazy = _senders.GetOrAdd(
            entityPath,
            static (path, pool) =>
                new Lazy<ServiceBusSender>(
                    () => pool._CreateSender(path),
                    LazyThreadSafetyMode.ExecutionAndPublication
                ),
            this
        );

        try
        {
            return lazy.Value;
        }
        catch
        {
            // Lazy<T> caches factory exceptions; evict the faulted entry so the next
            // publish to this destination retries instead of replaying the failure forever.
            _senders.TryRemove(new KeyValuePair<string, Lazy<ServiceBusSender>>(entityPath, lazy));
            throw;
        }
    }

    /// <summary>
    /// Disposes every materialized sender, then the shared client. Idempotent and safe when no
    /// sender (or no client) was ever created.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Capture AND null the client under the creation lock before draining: creation checks
        // _disposed inside the same lock, so a client is either visible here (and disposed below)
        // or was never created — no leak window. Nulling also stops the lock-free fast path from
        // handing out the disposed client to a publish racing shutdown; it falls into the slow
        // path and throws ObjectDisposedException instead.
        ServiceBusClient? client;

        lock (_clientLock)
        {
            client = _client;
            _client = null;
        }

        try
        {
            // Senders close independent AMQP links; drain them in parallel before the client.
            var senderDisposals = new List<Task>();

            foreach (var (entityPath, lazy) in _senders)
            {
                if (!lazy.IsValueCreated)
                {
                    continue;
                }

                _senders.TryRemove(entityPath, out _);
                senderDisposals.Add(lazy.Value.DisposeAsync().AsTask());
            }

            await Task.WhenAll(senderDisposals).ConfigureAwait(false);
        }
        finally
        {
            // Disposal is one-shot (the Interlocked guard above), so a faulted sender dispose
            // must not skip client disposal — a skipped client here would never be reclaimed.
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private ServiceBusSender _CreateSender(string entityPath)
    {
        var sender = _GetOrCreateClient().CreateSender(entityPath);
        _logger.SenderCreated(entityPath);

        return sender;
    }

    private ServiceBusClient _GetOrCreateClient()
    {
        // Fast path once the client exists; the lock only guards first creation. Creating a
        // ServiceBusClient performs no I/O (the AMQP connection is established lazily), so
        // holding the lock across the factory is cheap, and a factory failure caches nothing —
        // the next call retries.
        var client = Volatile.Read(ref _client);

        if (client is not null)
        {
            return client;
        }

        lock (_clientLock)
        {
            // Re-check disposal inside the lock: DisposeAsync reads _client under the same lock
            // after setting _disposed, so a creation racing shutdown either publishes the client
            // before the disposer reads it, or observes _disposed and throws — never leaks.
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (_client is null)
            {
                _client = _clientFactory(_options.Value);
                _logger.ClientCreated(_client.FullyQualifiedNamespace);
            }

            return _client;
        }
    }
}

internal static partial class AzureServiceBusClientPoolLog
{
    [LoggerMessage(
        EventId = 3021,
        EventName = "ServiceBusClientCreated",
        Level = LogLevel.Debug,
        Message = "Azure Service Bus client created for namespace {Namespace}."
    )]
    public static partial void ClientCreated(this ILogger logger, string @namespace);

    [LoggerMessage(
        EventId = 3022,
        EventName = "ServiceBusSenderCreated",
        Level = LogLevel.Debug,
        Message = "Azure Service Bus sender created for entity {EntityPath}."
    )]
    public static partial void SenderCreated(this ILogger logger, string entityPath);
}
