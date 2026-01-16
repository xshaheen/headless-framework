# feat: Implement Framework.Messaging.MassTransit

## Overview

Implement MassTransit provider for `Framework.Messaging.Abstractions` with full support for `IMessagePublisher` and `IMessageSubscriber`. Uses `IReceiveEndpointConnector` for runtime subscription.

## Research Insights

### IReceiveEndpointConnector - Runtime Subscription

From [MassTransit Discussion #5830](https://github.com/MassTransit/MassTransit/discussions/5830):

```csharp
var connector = provider.GetRequiredService<IReceiveEndpointConnector>();
var handle = connector.ConnectReceiveEndpoint("queue", (ctx, cfg) =>
{
    cfg.Handler<TMessage>(async context => await handler(context.Message));
});
await handle.Ready;
await handle.StopAsync();
```

## Technical Approach

### Files

```
src/Framework.Messaging.MassTransit/
├── MassTransitMessageBusAdapter.cs  (NEW)
├── Setup.cs                          (MODIFY)
└── Framework.Messaging.MassTransit.csproj (MODIFY)

tests/Framework.Messaging.MassTransit.Tests.Integration/
├── MassTransitMessageBusAdapterTests.cs
└── Framework.Messaging.MassTransit.Tests.Integration.csproj
```

### MassTransitMessageBusAdapter.cs

Corrected implementation with proper async disposal, thread safety, and error handling:

```csharp
namespace Framework.Messaging.MassTransit;

using global::MassTransit;

public sealed class MassTransitMessageBusAdapter : IMessageBus, IAsyncDisposable
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IReceiveEndpointConnector _connector;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<MassTransitMessageBusAdapter> _logger;
    private readonly ConcurrentDictionary<Type, SubscriptionState> _subscriptions = new();
    private int _disposed;

    public MassTransitMessageBusAdapter(
        IPublishEndpoint publishEndpoint,
        IReceiveEndpointConnector connector,
        IGuidGenerator guidGenerator,
        ILogger<MassTransitMessageBusAdapter> logger)
    {
        _publishEndpoint = publishEndpoint;
        _connector = connector;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    public async Task PublishAsync<T>(
        T message,
        PublishMessageOptions? options = null,
        CancellationToken cancellationToken = default
    ) where T : class
    {
        _ThrowIfDisposed();

        var uniqueId = options?.UniqueId ?? _guidGenerator.Create();

        await _publishEndpoint.Publish(message, ctx =>
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
        }, cancellationToken).AnyContext();
    }

    public async Task SubscribeAsync<TPayload>(
        Func<IMessageSubscribeMedium<TPayload>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    ) where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        _ThrowIfDisposed();

        var queueName = $"framework-{MessageName.GetFrom<TPayload>()}-{Guid.NewGuid():N}";

        var handle = _connector.ConnectReceiveEndpoint(queueName, (context, cfg) =>
        {
            cfg.Handler<TPayload>(async ctx =>
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing {MessageType}", typeof(TPayload).Name);
                    throw;
                }
            });
        });

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

        // Register cleanup - use synchronous callback to avoid async void
        state.Registration = cancellationToken.Register(
            () => _ = _RemoveSubscriptionAsync(typeof(TPayload)));
    }

    private async Task _RemoveSubscriptionAsync(Type payloadType)
    {
        if (!_subscriptions.TryRemove(payloadType, out var state))
            return;

        state.Registration?.Dispose();

        try
        {
            await state.Handle.StopAsync().AnyContext();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping subscription for {Type}", payloadType.Name);
        }
    }

    private static IDictionary<string, string>? _ExtractHeaders(Headers headers)
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

    private void _ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var subscriptions = _subscriptions.ToArray();
        _subscriptions.Clear();

        var stopTasks = subscriptions.Select(async kvp =>
        {
            kvp.Value.Registration?.Dispose();
            try
            {
                await kvp.Value.Handle.StopAsync().AnyContext();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping {Type}", kvp.Key.Name);
            }
        });

        await Task.WhenAll(stopTasks).AnyContext();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private sealed class SubscriptionState(HostReceiveEndpointHandle handle)
    {
        public HostReceiveEndpointHandle Handle { get; } = handle;
        public CancellationTokenRegistration? Registration { get; set; }
    }
}
```

### Setup.cs

```csharp
namespace Framework.Messaging.MassTransit;

[PublicAPI]
public static class MassTransitSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Framework messaging adapter for MassTransit.
        /// Call AFTER AddMassTransit().
        /// </summary>
        public IServiceCollection AddHeadlessMassTransitAdapter()
        {
            services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<IMessageBus>());
            services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<IMessageBus>());
            services.AddSingleton<IMessageBus, MassTransitMessageBusAdapter>();
            return services;
        }
    }
}
```

### Framework.Messaging.MassTransit.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Framework.Messaging.MassTransit</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MassTransit" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Framework.Messaging.Abstractions\Framework.Messaging.Abstractions.csproj" />
    <ProjectReference Include="..\Framework.Base\Framework.Base.csproj" />
  </ItemGroup>
</Project>
```

### MassTransitMessageBusAdapterTests.cs

```csharp
namespace Framework.Messaging.MassTransit.Tests.Integration;

public sealed class MassTransitMessageBusAdapterTests : TestBase
{
    [Fact]
    public async Task should_publish_message_with_headers()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.UsingInMemory())
            .AddHeadlessMassTransitAdapter()
            .AddSingleton<IGuidGenerator, SequentialGuidGenerator>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var publisher = provider.GetRequiredService<IMessagePublisher>();
        var correlationId = Guid.NewGuid();

        await publisher.PublishAsync(
            new TestMessage("test"),
            new PublishMessageOptions
            {
                UniqueId = Guid.NewGuid(),
                CorrelationId = correlationId,
                Headers = new Dictionary<string, string> { ["tenant"] = "acme" }
            }
        );

        (await harness.Published.Any<TestMessage>()).Should().BeTrue();
        var context = (await harness.Published.SelectAsync<TestMessage>().First()).Context;
        context.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task should_receive_message_via_subscribe_async()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)))
            .AddHeadlessMassTransitAdapter()
            .AddSingleton<IGuidGenerator, SequentialGuidGenerator>()
            .BuildServiceProvider(true);

        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync(AbortToken);

        var messageBus = provider.GetRequiredService<IMessageBus>();

        IMessageSubscribeMedium<TestMessage>? received = null;
        var tcs = new TaskCompletionSource<bool>();

        await messageBus.SubscribeAsync<TestMessage>(async (medium, ct) =>
        {
            received = medium;
            tcs.SetResult(true);
        }, AbortToken);

        var correlationId = Guid.NewGuid();
        await messageBus.PublishAsync(
            new TestMessage("test-value"),
            new PublishMessageOptions
            {
                UniqueId = Guid.NewGuid(),
                CorrelationId = correlationId,
            }
        );

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        received.Should().NotBeNull();
        received!.Payload.Value.Should().Be("test-value");
        received.CorrelationId.Should().Be(correlationId);

        await bus.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_cleanup_subscription_on_cancellation()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)))
            .AddHeadlessMassTransitAdapter()
            .AddSingleton<IGuidGenerator, SequentialGuidGenerator>()
            .BuildServiceProvider(true);

        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync(AbortToken);

        var messageBus = provider.GetRequiredService<IMessageBus>();

        using var cts = new CancellationTokenSource();

        await messageBus.SubscribeAsync<TestMessage>(async (_, _) => { }, cts.Token);

        // Cancel should cleanup subscription
        await cts.CancelAsync();
        await Task.Delay(100); // Allow cleanup to complete

        // Should be able to subscribe again after cancellation
        await messageBus.SubscribeAsync<TestMessage>(async (_, _) => { }, AbortToken);

        await bus.StopAsync(AbortToken);
    }
}

public sealed record TestMessage(string Value);
```

## Usage Examples

### Publishing

```csharp
public sealed class OrderService(IMessagePublisher publisher)
{
    public async Task CreateAsync(Order order, CancellationToken ct)
    {
        await publisher.PublishAsync(
            new OrderCreated(order.Id, order.Total),
            new PublishMessageOptions
            {
                UniqueId = Guid.NewGuid(),
                CorrelationId = order.Id,
            },
            ct
        );
    }
}
```

### Runtime Subscription

```csharp
public sealed class NotificationService(IMessageSubscriber subscriber) : IHostedService
{
    private CancellationTokenSource? _cts;

    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await subscriber.SubscribeAsync<OrderCreated>(async (medium, token) =>
        {
            await SendNotificationAsync(medium.Payload.OrderId, token);
        }, _cts.Token);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }
}
```

### DI Setup

```csharp
services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host("rabbitmq://localhost");
        cfg.ConfigureEndpoints(ctx);
    });
});

services.AddHeadlessMassTransitAdapter();
```

## Acceptance Criteria

### Functional

- [ ] `IMessagePublisher.PublishAsync<T>` publishes via MassTransit
- [ ] `IMessageSubscriber.SubscribeAsync<T>` works at runtime via `IReceiveEndpointConnector`
- [ ] `IMessageBus` combines both interfaces
- [ ] `PublishMessageOptions` maps to MassTransit headers
- [ ] Reuses `MessageSubscribeMedium<T>` from abstractions
- [ ] Subscription cleanup on cancellation token

### Non-Functional

- [ ] `IAsyncDisposable` implemented correctly
- [ ] Thread-safe disposal with `Interlocked`
- [ ] No `async void` - synchronous callbacks only
- [ ] `CancellationTokenRegistration` disposed
- [ ] Error logging in handlers
- [ ] `AnyContext()` on all async calls
- [ ] `sealed` classes, primary constructors

### Quality Gates

- [ ] `./build.sh Compile` passes
- [ ] Integration tests pass with InMemory transport
- [ ] `dotnet csharpier .` applied

## Header Mapping

| PublishMessageOptions | MassTransit SendContext |
|---|---|
| `UniqueId` | `MessageId` |
| `CorrelationId` | `CorrelationId` |
| `Headers["key"]` | `Headers.Set("key", value)` |

| MassTransit ConsumeContext | MessageSubscribeMedium |
|---|---|
| `MessageId` | `UniqueId` |
| `CorrelationId` | `CorrelationId` |
| `Headers` | `Properties` |
| `Message` | `Payload` |

## Dependencies

- MassTransit 9.0.0 (in Directory.Packages.props)
- Microsoft.Extensions.Logging.Abstractions
- Framework.Messaging.Abstractions
- Framework.Base (IGuidGenerator)

## References

- [MassTransit Discussion #5830](https://github.com/MassTransit/MassTransit/discussions/5830)
- [MassTransit IReceiveEndpointConnector](https://github.com/MassTransit/MassTransit/blob/develop/src/MassTransit/IReceiveEndpointConnector.cs)
- `src/Framework.Messaging.Abstractions/IMessageSubscribeMedium.cs:22` - reuse `MessageSubscribeMedium<T>`
- `src/Framework.Messaging.Foundatio/FoundatioMessageBusAdapter.cs:9` - reference pattern

---

## Changes from Review

1. **Removed Phase 2** (`IMessageHandler<T>`, `MassTransitConsumerAdapter`) - YAGNI, use MassTransit's `IConsumer<T>` directly
2. **Removed `MassTransitMessageSubscribeMedium`** - reuse existing `MessageSubscribeMedium<T>` from abstractions
3. **Added `IAsyncDisposable`** with proper async disposal
4. **Fixed thread safety** with `Interlocked` for disposed flag
5. **Fixed async void** - cancellation callbacks are synchronous, fire-and-forget with logging
6. **Added `CancellationTokenRegistration` tracking** to prevent memory leaks
7. **Added error handling** in handler callback with logging
8. **Added `ILogger` dependency** for proper error/warning logging
9. **Single test file** instead of three
