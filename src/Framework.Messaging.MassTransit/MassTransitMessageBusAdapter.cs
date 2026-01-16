// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Framework.Abstractions;
using Framework.Domains.Messages;
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

        try
        {
            await handle.Ready.AnyContext();
        }
        catch
        {
            await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
            throw;
        }

        // Check if already cancelled to avoid race condition
        if (cancellationToken.IsCancellationRequested)
        {
            await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
            return;
        }

        // Register cleanup with proper error handling
        state.Registration = cancellationToken.Register(() => _RemoveSubscriptionSync(typeof(TPayload)));
    }

    private void _RemoveSubscriptionSync(Type payloadType)
    {
        if (!_subscriptions.TryGetValue(payloadType, out var state))
        {
            return;
        }

        // Track removal task for proper cleanup and error handling
        var removalTask = _RemoveSubscriptionAsync(payloadType);
        state.PendingRemovalTask = removalTask;

        // Continue task to log unobserved exceptions
        _ = removalTask.ContinueWith(
            static (t, s) =>
            {
                var (log, type) = ((ILogger, Type))s!;
                if (t.Exception is not null)
                {
                    log.LogError(t.Exception, "Unhandled error removing subscription for {Type}", type.Name);
                }
            },
            (logger, payloadType),
            TaskScheduler.Default
        );
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
        _subscriptions.Clear();

        var stopTasks = subscriptions.Select(async kvp =>
        {
            // Wait for any pending removal task first
            if (kvp.Value.PendingRemovalTask is not null)
            {
                try
                {
                    await kvp.Value.PendingRemovalTask.AnyContext();
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Error in pending removal for {Type}", kvp.Key.Name);
                }
            }

            if (kvp.Value.Registration.HasValue)
            {
                await kvp.Value.Registration.Value.DisposeAsync().AnyContext();
            }

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
    }

    private sealed class SubscriptionState(HostReceiveEndpointHandle handle)
    {
        public HostReceiveEndpointHandle Handle { get; } = handle;
        public CancellationTokenRegistration? Registration { get; set; }
        public Task? PendingRemovalTask { get; set; }
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
