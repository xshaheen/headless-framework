// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Framework.Abstractions;
using Framework.Domain.Messages;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Framework.Messaging;

public sealed class MassTransitMessageBusAdapter(
    IPublishEndpoint publishEndpoint,
    IReceiveEndpointConnector connector,
    IGuidGenerator guidGenerator,
    ILogger<MassTransitMessageBusAdapter> logger
) : IMessageBus
{
    private readonly ConcurrentDictionary<Type, SubscriptionState> _subscriptions = new();
    private readonly ConcurrentBag<Task> _pendingCleanups = new();
    private int _disposed;

    public async Task PublishAsync<T>(
        T message,
        PublishMessageOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        _ThrowIfDisposed();

        var uniqueId = options?.UniqueId ?? guidGenerator.Create();

        await publishEndpoint
            .Publish(
                message,
                ctx =>
                {
                    ctx.MessageId = uniqueId;
                    ctx.CorrelationId = options?.CorrelationId ?? uniqueId;

                    if (options?.Headers is { Count: > 0 })
                    {
                        foreach (var (key, value) in options.Headers)
                        {
                            ctx.Headers.Set(key, value);
                        }
                    }
                },
                cancellationToken
            )
            .AnyContext();
    }

    public async Task SubscribeAsync<TPayload>(
        Func<IMessageSubscribeMedium<TPayload>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        _ThrowIfDisposed();

        var handle = connector.ConnectReceiveEndpoint(
            new TemporaryEndpointDefinition(),
            DefaultEndpointNameFormatter.Instance,
            (context, cfg) =>
            {
                cfg.Consumer(() => new DelegateConsumer<TPayload>(handler, logger));
            }
        );

        var state = new SubscriptionState(handle);

        if (!_subscriptions.TryAdd(typeof(TPayload), state))
        {
            await handle.StopAsync().AnyContext();
            throw new InvalidOperationException($"Already subscribed to {typeof(TPayload).Name}");
        }

        // Check if already cancelled to avoid race condition
        if (cancellationToken.IsCancellationRequested)
        {
            await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Register cleanup BEFORE Ready check to prevent leak if Ready throws
        state.Registration = cancellationToken.Register(() => _RemoveSubscriptionSync(typeof(TPayload)));

        try
        {
            await handle.Ready.AnyContext();
        }
        catch
        {
            await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
            throw;
        }
    }

    private void _RemoveSubscriptionSync(Type payloadType)
    {
        if (!_subscriptions.TryGetValue(payloadType, out var state))
        {
            return;
        }

        // Wrap async cleanup in Task.Run and track in bag for disposal coordination
        var cleanupTask = Task.Run(async () =>
        {
            try
            {
                await _RemoveSubscriptionAsync(payloadType).AnyContext();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error removing subscription for {Type}", payloadType.Name);
            }
        });

        _pendingCleanups.Add(cleanupTask);
    }

    private async Task _RemoveSubscriptionAsync(Type payloadType)
    {
        if (!_subscriptions.TryRemove(payloadType, out var state))
        {
            return;
        }

        if (state.Registration.HasValue)
        {
            await state.Registration.Value.DisposeAsync().AnyContext();
        }

        try
        {
            await state.Handle.StopAsync().AnyContext();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error stopping subscription for {Type}", payloadType.Name);
        }
    }

    private void _ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public void Dispose()
    {
        throw new NotSupportedException(
            "MassTransitMessageBusAdapter must be disposed asynchronously. Use DisposeAsync() instead."
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var subscriptions = _subscriptions.ToArray();

        // Phase 1: Dispose all cancellation registrations FIRST
        // This prevents new cancellation callbacks from starting
        foreach (var kvp in subscriptions)
        {
            if (kvp.Value.Registration.HasValue)
            {
                await kvp.Value.Registration.Value.DisposeAsync().AnyContext();
            }
        }

        // Phase 2: Clear subscriptions and stop all handles
        // Safe now that no more callbacks can fire
        _subscriptions.Clear();

        var stopTasks = subscriptions.Select(async kvp =>
        {
            try
            {
                await kvp.Value.Handle.StopAsync().AnyContext();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error stopping {Type}", kvp.Key.Name);
            }
        });

        await Task.WhenAll(stopTasks).AnyContext();

        // Phase 3: Wait for all pending cleanup tasks to complete
        await Task.WhenAll(_pendingCleanups).AnyContext();
    }

    private sealed class SubscriptionState(HostReceiveEndpointHandle handle)
    {
        public HostReceiveEndpointHandle Handle { get; } = handle;
        public CancellationTokenRegistration? Registration { get; set; }
    }

    private sealed class DelegateConsumer<TPayload>(
        Func<IMessageSubscribeMedium<TPayload>, CancellationToken, Task> handler,
        ILogger logger
    ) : IConsumer<TPayload>
        where TPayload : class
    {
        public async Task Consume(ConsumeContext<TPayload> ctx)
        {
            try
            {
                var uniqueId = ctx.MessageId ?? Guid.Empty;
                _ = Guid.TryParse(ctx.CorrelationId?.ToString(), out var correlationId);

                var medium = new MessageSubscribeMedium<TPayload>
                {
                    MessageKey = MessageName.GetFrom<TPayload>(),
                    Type = typeof(TPayload).FullName ?? typeof(TPayload).Name,
                    UniqueId = uniqueId,
                    CorrelationId = correlationId != Guid.Empty ? correlationId : null,
                    Properties = _ExtractHeaders(ctx.Headers),
                    Payload = ctx.Message,
                };

                await handler(medium, ctx.CancellationToken).AnyContext();
            }
            catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error processing {MessageType}", typeof(TPayload).Name);
                throw;
            }
        }

        private static Dictionary<string, string>? _ExtractHeaders(Headers headers)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var header in headers.GetAll())
            {
                if (header.Value is string strValue)
                {
                    dict[header.Key] = strValue;
                }
            }

            return dict.Count > 0 ? dict : null;
        }
    }
}
