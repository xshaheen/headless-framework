# IConsume<T> Part 1: Core Foundation

**Priority**: CRITICAL - Must execute first
**Dependencies**: None
**Estimated Effort**: 2-3 days

## Goal

Remove old `[CapSubscribe]` pattern and implement basic `IConsume<T>` registration with manual topic/group configuration.

## Scope

**In Scope:**
- Delete old pattern (`IConsumer`, `[CapSubscribe]`, `[FromCap]`, reflection invoker)
- Basic `AddConsumer<T>()` registration
- Manual topic/group configuration
- `CompiledMessageDispatcher` integration
- Basic end-to-end message flow

**Out of Scope:**
- Assembly scanning (Part 2)
- Conventions (Part 2)
- Retry policies (Part 3)
- Filters (Part 4)
- Validation (Part 5)

## Implementation

### Phase 1: Cleanup

**Delete:**
- [ ] `src/Framework.Messages.Abstractions/IConsumer.cs`
- [ ] `src/Framework.Messages.Abstractions/Attributes.cs` (`[CapSubscribe]`, `[FromCap]`, `[Topic]`)
- [ ] Reflection parameter binding in `ISubscribeInvoker.Default.cs`
- [ ] Old discovery logic in `IConsumerServiceSelector.Default.cs`
- [ ] `tests/Framework.Messages.Core.Tests.Unit/ConsumerServiceSelectorTest.cs`

### Phase 2: Core Abstractions

**Create in Framework.Messages.Abstractions:**

```csharp
// IMessagingBuilder.cs (minimal)
public interface IMessagingBuilder
{
    IMessagingBuilder AddConsumer<TConsumer>(Action<IConsumerConfigurator<TConsumer>>? configure = null)
        where TConsumer : class;
}

// IConsumerConfigurator.cs (minimal)
public interface IConsumerConfigurator<TConsumer>
    where TConsumer : class
{
    void Topic(string topic);
    void Group(string? group);
    void Concurrency(int count);
}

// MessagingBuilderExtensions.cs
public static class MessagingBuilderExtensions
{
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        Action<IMessagingBuilder> configure)
    {
        var builder = new MessagingBuilder(services);
        configure(builder);
        return services;
    }
}
```

### Phase 3: Core Implementation

**Create in Framework.Messages.Core:**

```csharp
// ConsumerMetadata.cs
public sealed record ConsumerMetadata(
    Type MessageType,
    Type ConsumerType,
    string Topic,
    string? Group,
    int Concurrency
);

// ConsumerRegistry.cs
public sealed class ConsumerRegistry
{
    private readonly Dictionary<Type, List<ConsumerMetadata>> _consumers = new();

    public void Register(ConsumerMetadata metadata)
    {
        if (!_consumers.TryGetValue(metadata.MessageType, out var list))
        {
            list = [];
            _consumers[metadata.MessageType] = list;
        }
        list.Add(metadata);
    }

    public IReadOnlyList<ConsumerMetadata> GetAll() =>
        _consumers.Values.SelectMany(x => x).ToList();

    public ConsumerMetadata? GetByMessageType(Type messageType) =>
        _consumers.TryGetValue(messageType, out var list) ? list.FirstOrDefault() : null;
}

// ConsumerConfigurator.cs
internal sealed class ConsumerConfigurator<TConsumer> : IConsumerConfigurator<TConsumer>
    where TConsumer : class
{
    private string? _topic;
    private string? _group;
    private int _concurrency = 1;

    public void Topic(string topic) => _topic = topic;
    public void Group(string? group) => _group = group;
    public void Concurrency(int count) => _concurrency = count;

    internal ConsumerMetadata Build(Type messageType, Type consumerType)
    {
        return new ConsumerMetadata(
            messageType,
            consumerType,
            _topic ?? messageType.Name,  // Default: TMessage.Name
            _group,
            _concurrency
        );
    }
}

// MessagingBuilder.cs
public sealed class MessagingBuilder(IServiceCollection services) : IMessagingBuilder
{
    private readonly ConsumerRegistry _registry = new();

    public IMessagingBuilder AddConsumer<TConsumer>(
        Action<IConsumerConfigurator<TConsumer>>? configure = null)
        where TConsumer : class
    {
        // Find IConsume<TMessage> interface
        var consumeInterface = typeof(TConsumer).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                               i.GetGenericTypeDefinition() == typeof(IConsume<>));

        if (consumeInterface == null)
        {
            throw new InvalidOperationException(
                $"{typeof(TConsumer).Name} does not implement IConsume<T>");
        }

        var messageType = consumeInterface.GetGenericArguments()[0];

        // Configure
        var configurator = new ConsumerConfigurator<TConsumer>();
        configure?.Invoke(configurator);

        // Build metadata
        var metadata = configurator.Build(messageType, typeof(TConsumer));
        _registry.Register(metadata);

        // Register in DI
        var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
        services.AddScoped(serviceType, typeof(TConsumer));

        // Register registry as singleton
        services.TryAddSingleton(_registry);

        return this;
    }
}
```

### Phase 4: Discovery & Invocation

**Update IConsumerServiceSelector.Default.cs:**

```csharp
protected override IReadOnlyList<ConsumerExecutorDescriptor> FindConsumersFromInterfaceTypes(
    IServiceCollection serviceCollection)
{
    var executors = new List<ConsumerExecutorDescriptor>();
    var registry = serviceProvider.GetRequiredService<ConsumerRegistry>();

    foreach (var metadata in registry.GetAll())
    {
        var descriptor = new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = metadata.ConsumerType.GetTypeInfo(),
            ImplTypeInfo = metadata.ConsumerType.GetTypeInfo(),
            MethodInfo = typeof(IConsume<>)
                .MakeGenericType(metadata.MessageType)
                .GetMethod(nameof(IConsume<object>.Consume))!,
            TopicName = metadata.Topic,
            GroupName = metadata.Group,
            Parameters = []
        };

        executors.Add(descriptor);
    }

    return executors;
}
```

**Simplify ISubscribeInvoker.Default.cs:**

```csharp
public async Task<OperateResult> InvokeAsync(
    ConsumerContext context,
    CancellationToken cancellationToken)
{
    var descriptor = context.ConsumerDescriptor;
    var dispatcher = _serviceProvider.GetRequiredService<CompiledMessageDispatcher>();

    // Extract TMessage from method parameter
    var messageType = descriptor.MethodInfo.GetParameters()[0]
        .ParameterType.GetGenericArguments()[0];

    await using var scope = _serviceProvider.CreateAsyncScope();

    // Resolve handler
    var handlerType = typeof(IConsume<>).MakeGenericType(messageType);
    var handler = scope.ServiceProvider.GetRequiredService(handlerType);

    // Build ConsumeContext<T>
    var consumeContext = CreateConsumeContext(context.DeliverMessage, messageType);

    // Dispatch
    await dispatcher.DispatchAsync(handler, consumeContext, cancellationToken);

    return OperateResult.Success;
}

private object CreateConsumeContext(MediumMessage message, Type messageType)
{
    var messageInstance = JsonSerializer.Deserialize(message.Value, messageType);
    var contextType = typeof(ConsumeContext<>).MakeGenericType(messageType);

    return Activator.CreateInstance(contextType, new object[]
    {
        messageInstance,
        Guid.Parse(message.GetId()),
        message.GetCorrelationId(),
        new MessageHeader(message.Headers),
        DateTimeOffset.FromUnixTimeSeconds(message.Added),
        message.Origin
    })!;
}
```

## Testing

### Unit Tests

```csharp
// MessagingBuilderTest.cs
public class MessagingBuilderTest
{
    [Fact]
    public void should_register_consumer_with_default_topic()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m => m.AddConsumer<OrderCreatedHandler>());

        var sp = services.BuildServiceProvider();
        var handler = sp.GetService<IConsume<OrderCreated>>();

        handler.Should().NotBeNull();
        handler.Should().BeOfType<OrderCreatedHandler>();
    }

    [Fact]
    public void should_register_consumer_with_custom_topic()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.AddConsumer<OrderCreatedHandler>(c =>
            {
                c.Topic("orders.created");
                c.Group("order-processing");
                c.Concurrency(5);
            });
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetService<ConsumerRegistry>();

        var metadata = registry!.GetByMessageType(typeof(OrderCreated));
        metadata.Should().NotBeNull();
        metadata!.Topic.Should().Be("orders.created");
        metadata.Group.Should().Be("order-processing");
        metadata.Concurrency.Should().Be(5);
    }

    [Fact]
    public void should_throw_when_type_does_not_implement_iconsume()
    {
        var services = new ServiceCollection();
        var act = () => services.AddMessaging(m => m.AddConsumer<InvalidHandler>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not implement IConsume<T>*");
    }
}

public sealed class OrderCreatedHandler : IConsume<OrderCreated>
{
    public ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public sealed class InvalidHandler { }
```

### Integration Test

```csharp
// IConsumeIntegrationTest.cs
public class IConsumeIntegrationTest
{
    [Fact]
    public async Task should_invoke_handler_when_message_received()
    {
        // Setup
        var services = new ServiceCollection();
        services.AddSingleton<IOrderService, FakeOrderService>();
        services.AddMessaging(m =>
        {
            m.AddConsumer<OrderCreatedHandler>(c =>
            {
                c.Topic("orders.created");
                c.Group("test");
            });
        });

        var sp = services.BuildServiceProvider();

        // Simulate message receipt
        var message = new MediumMessage
        {
            Value = JsonSerializer.Serialize(new OrderCreated { OrderId = 123 }),
            Origin = "orders.created",
            Headers = new Dictionary<string, string>()
        };

        var invoker = sp.GetRequiredService<ISubscribeInvoker>();
        var descriptor = /* get from selector */;
        var context = new ConsumerContext(descriptor, message);

        // Act
        var result = await invoker.InvokeAsync(context, CancellationToken.None);

        // Assert
        result.Should().Be(OperateResult.Success);
        var orderService = sp.GetRequiredService<IOrderService>() as FakeOrderService;
        orderService!.ProcessedOrders.Should().Contain(123);
    }
}
```

## Acceptance Criteria

- [ ] Old pattern completely removed
- [ ] `AddConsumer<T>()` registers handler in DI and registry
- [ ] Default topic = `TMessage.Name`
- [ ] Can override topic, group, concurrency
- [ ] `IConsumerServiceSelector` finds registered handlers
- [ ] `ISubscribeInvoker` uses `CompiledMessageDispatcher`
- [ ] End-to-end message flow works
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Coverage ≥85%

## Usage Example

```csharp
// Handler
public sealed class OrderCreatedHandler(IOrderService orderService)
    : IConsume<OrderCreated>
{
    public async ValueTask Consume(
        ConsumeContext<OrderCreated> context,
        CancellationToken ct)
    {
        await orderService.ProcessAsync(context.Message, ct);
    }
}

// Registration
services.AddMessaging(messaging =>
{
    messaging.AddConsumer<OrderCreatedHandler>(c =>
    {
        c.Topic("orders.created");
        c.Group("order-processing");
        c.Concurrency(5);
    });

    messaging.AddConsumer<PaymentHandler>(c =>
    {
        c.Topic("payments.received");
        c.Group("payment-processing");
    });
});
```

## Files Changed

**Deleted:**
- `src/Framework.Messages.Abstractions/IConsumer.cs`
- `src/Framework.Messages.Abstractions/Attributes.cs`
- `tests/Framework.Messages.Core.Tests.Unit/ConsumerServiceSelectorTest.cs`

**Created:**
- `src/Framework.Messages.Abstractions/IMessagingBuilder.cs`
- `src/Framework.Messages.Abstractions/IConsumerConfigurator.cs`
- `src/Framework.Messages.Abstractions/MessagingBuilderExtensions.cs`
- `src/Framework.Messages.Core/MessagingBuilder.cs`
- `src/Framework.Messages.Core/ConsumerConfigurator.cs`
- `src/Framework.Messages.Core/ConsumerMetadata.cs`
- `src/Framework.Messages.Core/ConsumerRegistry.cs`
- `tests/Framework.Messages.Core.Tests.Unit/MessagingBuilderTest.cs`
- `tests/Framework.Messages.Core.Tests.Unit/IConsumeIntegrationTest.cs`

**Modified:**
- `src/Framework.Messages.Core/Internal/IConsumerServiceSelector.Default.cs`
- `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs`
