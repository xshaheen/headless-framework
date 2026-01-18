# Replace [CapSubscribe] with IConsume&lt;T&gt; Interface Pattern

## ⚠️ THIS PLAN HAS BEEN DIVIDED INTO SUB-PLANS

**See**: `refactor-iconsume-0-execution-plan.md` for execution strategy

**Sub-Plans**:
1. **Part 1 - Core Foundation** (CRITICAL, ~3 days) - `refactor-iconsume-1-core-foundation.md`
2. **Part 2 - Conventions & Scanning** (HIGH, ~2 days) - `refactor-iconsume-2-conventions-scanning.md`
3. **Part 3 - Retry & Error Handling** (MEDIUM, ~1-2 days) - `refactor-iconsume-3-retry-error-handling.md`
4. **Part 4 - Filters** (MEDIUM-LOW, ~1 day) - `refactor-iconsume-4-filters.md`
5. **Part 5 - Validation** (LOW, ~0.5 hour) - `refactor-iconsume-5-validation.md`

**Recommended**: Execute Parts 1 & 2 first (~1 week), evaluate Parts 3-5 later.

**⚠️ IMPORTANT - Unified API Architecture**:
Parts 3, 4, and 5 have been updated to reflect pragmatic decisions from expert review:
- **Part 3 (Retry/DLQ)**: ✅ **UPDATED** - Thin wrapper over CAP's `FailedRetryCount` and `FailedThresholdCallback` (150 LOC, 0.5 day)
- **Part 4 (Filters)**: ✅ **UPDATED** - Thin wrapper over CAP's `SubscriberFilters` (50 LOC, 0.25 day)
- **Part 5 (Validation)**: ✅ **UPDATED** - Minimal 20-line inline check (duplicate detection + empty config warning, 0.5 hour)

**Total savings**: ~4-5 days effort, ~1400 LOC avoided by wrapping CAP instead of reimplementing.

---

## Overview

Replace attribute-based message handling (`[CapSubscribe]` + `IConsumer` marker) with type-safe interface pattern (`IConsume<TMessage>`) for improved compile-time safety, better IDE support, and enhanced performance through compiled expression trees.

**CRITICAL ARCHITECTURAL DECISION**: `AddMessaging()` is the **single unified API** that wraps and configures CAP internally. Users never call `AddCap()` directly. All messaging configuration (handlers, retry, filters, DLQ) goes through the `AddMessaging()` builder.

## Problem Statement / Motivation

**Current Limitations:**
- **Runtime discovery**: Reflection-based method scanning adds startup overhead
- **No type safety**: Message parameter types only validated at runtime
- **Weak IDE support**: No IntelliSense for topic names or parameters
- **Difficult testing**: Attribute-decorated methods hard to isolate
- **Performance**: `MethodInfo.Invoke` ~100x slower than direct calls

**Example (Current Pattern):**
```csharp
// Framework.Messages.Core.Tests.Unit/ConsumerServiceSelectorTest.cs:161-204
public class OrderHandler : IConsumer
{
    [CapSubscribe("orders.created")]
    public Task HandleOrderCreated(OrderCreated order, [FromCap] CapHeader headers)
    {
        // Runtime parameter binding, no compile-time checks
    }
}
```

**New Pattern (Commit 407c4029):**
```csharp
// Framework.Messages.Abstractions/IConsume.cs:42-50
public class OrderCreatedHandler : IConsume<OrderCreated>
{
    public async ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken ct)
    {
        // Compile-time type safety, rich context object
        var order = context.Message;
        var messageId = context.MessageId;
    }
}
```

**Benefits:**
- Compile-time type checking
- 5-10x faster handler invocation (compiled expressions vs. reflection)
- Better testability (inject `ConsumeContext<T>` in tests)
- Improved DX (IntelliSense, refactoring, navigation)

**References:**
- Performance: [10x Faster with Compiled Expressions](https://particular.net/blog/10x-faster-execution-with-compiled-expression-trees)
- Pattern: [MassTransit Consumers](https://masstransit.io/documentation/concepts/consumers)

## Proposed Solution

### High-Level Approach

1. **Remove Old Pattern** (delete `IConsumer`, `[CapSubscribe]`, `[FromCap]`, reflection-based invoker)
2. **Implement Builder API** (`IMessagingBuilder`, `IConsumerBuilder<T>`)
3. **Replace Handler Discovery** (only find `IConsume<T>` implementations)
4. **Integrate Dispatcher** (`CompiledMessageDispatcher` as only invocation path)

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                 Application Startup                          │
│              services.AddMessaging(...)                      │
└────────────────────┬────────────────────────────────────────┘
                     │
        ┌────────────▼────────────────────┐
        │  IMessagingBuilder              │
        │  - AddConsumer<T>()             │
        │  - AddConsumersFromAssembly()   │
        │  - ConfigureConventions()       │
        │  - ConfigureCap() ◄────────────┼─── Configures CAP internally
        └────────┬───────────────────────┘
                 │
                 ├─► Configures retry (maps to CAP.FailedRetryCount)
                 ├─► Configures DLQ (maps to CAP.FailedThresholdCallback)
                 ├─► Configures filters (maps to CAP.SubscriberFilters)
                 └─► Registers IConsume<T> with DI
                 │
    ┌────────────▼─────────────────────────┐
    │  IConsumerServiceSelector            │
    │  - SelectCandidates() finds:         │
    │    • IConsume<T> only                │
    └────────┬─────────────────────────────┘
             │
┌────────────▼──────────────────────────────────────┐
│          Message Received from Broker             │
│               (via CAP)                           │
└────────┬──────────────────────────────────────────┘
         │
         ▼
┌───────────────────────────────┐
│ CompiledMessageDispatcher     │
│ (Compiled Expressions)         │
│ - Zero reflection              │
│ - Type-safe invocation         │
└───────────────────────────────┘
```

**Key Points**:
- `AddMessaging()` wraps `AddCap()` - users never call CAP directly
- Retry/DLQ/Filters delegate to CAP's implementations
- No duplicate infrastructure - thin wrapper over CAP
- Single configuration API for all messaging concerns

### Implementation Phases

**Phase 1: Cleanup** (Remove Old Pattern)
- Delete `IConsumer.cs`, `Attributes.cs` (`[CapSubscribe]`, `[FromCap]`, `[Topic]`)
- Remove reflection-based parameter binding in `ISubscribeInvoker`
- Remove old discovery logic from `IConsumerServiceSelector`
- Delete old tests for attribute-based pattern

**Phase 2: Builder API** (Framework.Messages.Abstractions & Core)
- Implement `MessagingBuilder`, `ConsumerBuilder<T>`, `ConsumerRegistry`
- Complete `IMessagingBuilder` and `IConsumerBuilder<T>` contracts
- Add `AddMessaging()` extension method

**Phase 3: Discovery & Invocation** (Framework.Messages.Core)
- Rewrite `IConsumerServiceSelector` to only find `IConsume<T>`
- Replace `ISubscribeInvoker` to only use `CompiledMessageDispatcher`
- Integrate filter pipeline with new pattern

**Phase 4: Testing**
- Test harness (`ConsumeContextBuilder<T>`)
- Unit tests for builder, registry, discovery
- Integration tests for end-to-end flow
- Performance benchmarks

## Technical Approach

### Component Details

#### 1. Builder API Implementation

**Files to Create:**
- `src/Framework.Messages.Core/MessagingBuilder.cs`
- `src/Framework.Messages.Core/ConsumerBuilder.cs`
- `src/Framework.Messages.Core/ConsumerRegistry.cs`

**MessagingBuilder.cs (Pseudo-code):**
```csharp
namespace Headless.Framework.Messages;

public sealed class MessagingBuilder(IServiceCollection services) : IMessagingBuilder
{
    private readonly ConsumerRegistry _registry = new();

    public IMessagingBuilder ScanConsumers(Assembly assembly)
    {
        // Find all IConsume<T> implementations
        var consumerTypes = assembly.GetTypes()
            .Where(t => t.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>)));

        foreach (var type in consumerTypes)
        {
            // Register with DI as scoped
            // Add to registry
        }
        return this;
    }

    public IConsumerBuilder<TConsumer> Consumer<TConsumer>()
        where TConsumer : class
    {
        return new ConsumerBuilder<TConsumer>(services, _registry);
    }

    public IMessagingBuilder WithTopicMapping<TMessage>(string topic)
        where TMessage : class
    {
        _registry.MapTopic<TMessage>(topic);
        return this;
    }
}
```

**ConsumerBuilder.cs (Pseudo-code):**
```csharp
public sealed class ConsumerBuilder<TConsumer>(
    IServiceCollection services,
    ConsumerRegistry registry)
    : IConsumerBuilder<TConsumer>
    where TConsumer : class
{
    private string? _topic;
    private string? _group;
    private byte _concurrency = 1;

    public IConsumerBuilder<TConsumer> Topic(string topic, bool isPartial = false)
    {
        _topic = topic;
        return this;
    }

    public IConsumerBuilder<TConsumer> Group(string group)
    {
        _group = group;
        return this;
    }

    public IConsumerBuilder<TConsumer> WithConcurrency(byte concurrency)
    {
        _concurrency = concurrency;
        return this;
    }

    public IMessagingBuilder Build()
    {
        // Extract TMessage from IConsume<TMessage>
        var messageType = typeof(TConsumer).GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
            .GetGenericArguments()[0];

        // Default topic: typeof(TMessage).Name
        _topic ??= messageType.Name;

        // Register with DI
        services.AddScoped(typeof(IConsume<>).MakeGenericType(messageType), typeof(TConsumer));

        // Add to registry
        registry.Register(messageType, typeof(TConsumer), _topic, _group, _concurrency);

        return new MessagingBuilder(services);
    }
}
```

**ConsumerRegistry.cs (Pseudo-code):**
```csharp
public sealed class ConsumerRegistry
{
    private readonly ConcurrentDictionary<Type, ConsumerMetadata> _consumers = new();

    public void Register(Type messageType, Type consumerType, string topic, string? group, byte concurrency)
    {
        _consumers[messageType] = new ConsumerMetadata(consumerType, topic, group, concurrency);
    }

    public ConsumerMetadata? Get(Type messageType) =>
        _consumers.TryGetValue(messageType, out var metadata) ? metadata : null;

    public IEnumerable<ConsumerMetadata> GetAll() => _consumers.Values;
}

public sealed record ConsumerMetadata(
    Type ConsumerType,
    string Topic,
    string? Group,
    byte Concurrency
);
```

#### 2. Simplified Discovery

**File to Replace:**
- `src/Framework.Messages.Core/Internal/IConsumerServiceSelector.Default.cs`

**New Implementation:**
```csharp
protected override IReadOnlyList<ConsumerExecutorDescriptor> FindConsumersFromInterfaceTypes(
    IServiceCollection serviceCollection)
{
    var executors = new List<ConsumerExecutorDescriptor>();

    // Find IConsume<T> implementations only
    var consumeServices = serviceCollection
        .Where(x => x.ServiceType.IsGenericType &&
                    x.ServiceType.GetGenericTypeDefinition() == typeof(IConsume<>));

    // Get registry for metadata
    var registry = serviceProvider.GetRequiredService<ConsumerRegistry>();

    foreach (var service in consumeServices)
    {
        var messageType = service.ServiceType.GetGenericArguments()[0];
        var consumerType = service.ImplementationType;
        var metadata = registry.Get(messageType);

        var descriptor = new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = consumerType.GetTypeInfo(),
            ImplTypeInfo = consumerType.GetTypeInfo(),
            MethodInfo = typeof(IConsume<>).MakeGenericType(messageType)
                .GetMethod(nameof(IConsume<object>.Consume))!,
            TopicName = metadata?.Topic ?? messageType.Name,
            GroupName = metadata?.Group,
            Parameters = [] // No parameter binding needed - ConsumeContext has everything
        };

        executors.Add(descriptor);
    }

    return executors;
}
```

#### 3. Simplified Dispatcher Integration

**File to Replace:**
- `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs`

**New Implementation:**
```csharp
public async Task<OperateResult> InvokeAsync(ConsumerContext context, CancellationToken cancellationToken)
{
    var descriptor = context.ConsumerDescriptor;
    var dispatcher = _serviceProvider.GetRequiredService<CompiledMessageDispatcher>();

    // Extract message type from descriptor
    var messageType = descriptor.MethodInfo.GetParameters()[0]
        .ParameterType.GetGenericArguments()[0]; // ConsumeContext<TMessage> -> TMessage

    await using var scope = _serviceProvider.CreateAsyncScope();

    // Resolve IConsume<TMessage>
    var handlerType = typeof(IConsume<>).MakeGenericType(messageType);
    var handler = scope.ServiceProvider.GetRequiredService(handlerType);

    // Build ConsumeContext<TMessage>
    var consumeContext = CreateConsumeContext(context.DeliverMessage, messageType);

    // Apply filters
    var filter = scope.ServiceProvider.GetService<IConsumeFilter>();
    if (filter != null)
    {
        var executingCtx = new ExecutingContext(consumeContext, cancellationToken);
        await filter.OnSubscribeExecutingAsync(executingCtx);
    }

    // Dispatch
    await dispatcher.DispatchAsync(handler, consumeContext, cancellationToken);

    if (filter != null)
    {
        var executedCtx = new ExecutedContext(consumeContext);
        await filter.OnSubscribeExecutedAsync(executedCtx);
    }

    return OperateResult.Success;
}

private object CreateConsumeContext(MediumMessage message, Type messageType)
{
    // Deserialize message.Value to TMessage
    var messageInstance = JsonSerializer.Deserialize(message.Value, messageType);

    // Build ConsumeContext<TMessage>
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

#### 4. Topic Resolution Strategy

**Convention:** `typeof(TMessage).Name`
**Override:** Via builder `.Topic("custom.topic")`
**Global mapping:** Via `builder.WithTopicMapping<TMessage>("topic")`

**Example:**
```csharp
services.AddMessaging(messaging =>
{
    // Default: subscribes to "OrderCreated" topic
    messaging.ScanConsumers(Assembly.GetExecutingAssembly());

    // Override: subscribes to "orders.created"
    messaging.Consumer<OrderCreatedHandler>()
        .Topic("orders.created")
        .Group("order-processing")
        .Build();

    // Global mapping affects all consumers
    messaging.WithTopicMapping<PaymentReceived>("payments.received");
});
```

## Acceptance Criteria

### Functional Requirements

- [ ] Old pattern completely removed (`IConsumer`, `[CapSubscribe]`, `[FromCap]`, `[Topic]`)
- [ ] `IMessagingBuilder` and `IConsumerBuilder<T>` fully implemented
- [ ] Assembly scanning discovers `IConsume<T>` implementations only
- [ ] Topic names derived from message type or builder config
- [ ] `CompiledMessageDispatcher` invokes handlers with correct context
- [ ] Scoped DI resolution per message
- [ ] Filters integrate with `IConsume<T>` handlers
- [ ] Error handling triggers CAP retry mechanism

### Non-Functional Requirements

- [ ] Handler invocation ≥5x faster than old reflection approach (benchmark)
- [ ] Startup time improved (simpler discovery, no reflection)
- [ ] Memory usage reduced (no parameter descriptor caching)

### Testing Requirements

- [ ] Unit tests for builder API
- [ ] Unit tests for simplified discovery
- [ ] Integration tests for end-to-end message flow
- [ ] Test harness with `ConsumeContextBuilder<T>`
- [ ] Performance benchmarks (old reflection vs. new compiled)

### Quality Gates

- [ ] Line coverage ≥85%, branch coverage ≥80%
- [ ] XML docs for all public APIs
- [ ] README.md updated in both projects

## Success Metrics

**Performance:**
- Handler invocation latency reduced by 80%+
- Startup time improved by 20%+ (simpler discovery)
- Memory usage reduced (no reflection caching overhead)

**Developer Experience:**
- Type-safe message handling with compile-time checks
- Better IDE support (IntelliSense, navigation, refactoring)
- Simpler testing (inject `ConsumeContext<T>` directly)

## Dependencies & Risks

### Dependencies

**Internal:**
- `Framework.Messages.Abstractions` (IConsume&lt;T&gt;, ConsumeContext&lt;T&gt; exist)
- `Framework.Messages.Core` (CompiledMessageDispatcher exists)
- No new external dependencies required

**External:**
- No changes to CAP library configuration
- No broker (RabbitMQ, Kafka, etc.) changes required

### Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Topic routing errors** | MEDIUM | Validation in registry, clear error messages, unit tests |
| **Filter incompatibility** | LOW | Adapt `IConsumeFilter` to new pattern, integration tests |
| **Dispatcher bugs** | HIGH | Extensive unit tests, integration tests, benchmarks |
| **DI scoping issues** | MEDIUM | Test with various lifetimes, document constraints |

### Design Decisions (From Enhanced API)

**Resolved:**
1. **Multiple handlers per message type**: Yes - via different consumer groups (competing consumers pattern)
2. **Handlers implementing multiple `IConsume<T>`**: Yes - each interface registered independently
3. **DI lifetime**: Scoped only (enforced by builder, resolved per-message)
4. **Scope creation**: `ISubscribeInvoker` creates/disposes async scope per message
5. **Unregistered messages**: Log error + nack (configurable via validation rules)
6. **Retry policy**: Global config with per-consumer override via fluent API
7. **Consumer groups**: Optional - can be set via conventions or per-handler
8. **DLQ**: Configurable via `ConfigureDeadLetterQueue()` API
9. **Topic naming**: Convention-based (type name) with global/per-handler overrides
10. **Filters**: Global and per-consumer via fluent API
11. **Validation**: Optional strict mode via `ConfigureValidation()`
12. **Assembly scanning**: With filtering via predicates, namespaces, explicit exclusions

**API Conventions:**
- No `.Build()` - implicit registration
- Lambda configuration over method chaining
- `Add*` prefix for registration methods
- `Configure*` prefix for global configuration
- Type parameter first for inference (`AddConsumer<THandler>` auto-detects `TMessage`)

## References & Research

### Internal Implementation

**Current Patterns:**
- `src/Framework.Messages.Core/Internal/IConsumerServiceSelector.Default.cs:77-153` - Handler discovery
- `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs:48-80` - Handler invocation
- `src/Framework.Messages.Abstractions/IConsume.cs:42-50` - New interface
- `src/Framework.Messages.Abstractions/ConsumeContext.cs:21-171` - Rich context object
- `src/Framework.Messages.Core/Internal/CompiledMessageDispatcher.cs:98-180` - Compiled dispatcher

**Test Examples:**
- `tests/Framework.Messages.Core.Tests.Unit/ConsumerServiceSelectorTest.cs:161-204` - Discovery tests
- `tests/Framework.Messages.Core.Tests.Unit/ConsumerServiceSelectorTest.cs:232-250` - Multiple handlers

### External Documentation

**Performance Research:**
- [10x Faster Execution with Compiled Expression Trees](https://particular.net/blog/10x-faster-execution-with-compiled-expression-trees)
- [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler) - Used in `CompiledMessageDispatcher`
- [High-Performance Calculations with Expression Trees](https://blog.nashtechglobal.com/high-performance-calculations-in-net-using-expression-trees-and-caching/)

**Pattern References:**
- [MassTransit Consumers](https://masstransit.io/documentation/concepts/consumers) - Similar `IConsumer<T>` pattern
- [Wolverine Message Handlers](https://wolverinefx.net/guide/handlers/) - Convention-based discovery
- [Mediator Source Generator](https://github.com/martinothamar/Mediator) - Compile-time registration

**.NET 10 Features:**
- [What's new in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview) - Source generators, UnsafeAccessor
- [Keyed Services in .NET 8+](https://www.bensampica.com/post/keyedservices/) - DI patterns
- [Performance Improvements in .NET 10](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)

### Related Work

**Similar Projects:**
- CAP's attribute-based approach: [DotNetCore/CAP](https://github.com/dotnetcore/CAP)
- Type-safe alternative: [MassTransit](https://github.com/MassTransit/MassTransit)
- Convention-based: [Wolverine](https://github.com/JasperFx/wolverine)

**Project Commits:**
- `407c4029` - feat(messages): add ConsumeContext and IConsume interface
- `c3f700f0` - refactor(transaction): rename CapTransactionHolder

## Fluent Configuration API (Enhanced)

### Design Principles
- **No `.Build()`** - Registration implicit on method call
- **Convention over configuration** - Smart defaults, override when needed
- **Type safety** - Compile-time validation where possible
- **Lambda configuration** - Cleaner than method chaining
- **Clear intent** - Method names describe what they do

### Basic Registration

```csharp
// Program.cs
services.AddMessaging(messaging =>
{
    // CAP transport configuration (REQUIRED - replaces AddCap())
    messaging.ConfigureCap(cap =>
    {
        cap.UseRabbitMQ(options =>
        {
            options.HostName = "localhost";
            options.UserName = "guest";
            options.Password = "guest";
        });

        cap.UseSqlServer(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("Default");
        });
    });

    // Simplest consumer - auto-discover from handler type
    messaging.AddConsumer<OrderCreatedHandler>();
    // Topic: "OrderCreated" (from TMessage type name)
    // Group: null (broker default)
    // Concurrency: 1

    // With configuration
    messaging.AddConsumer<OrderCreatedHandler>(c =>
    {
        c.Topic("orders.created");
        c.Group("order-processing");
        c.Concurrency(5);
    });
});

// NOTE: Users NEVER call services.AddCap() directly
// AddMessaging() configures CAP internally
```

### Assembly Scanning with Conventions

```csharp
services.AddMessaging(messaging =>
{
    // Scan with defaults
    messaging.AddConsumersFromAssembly(Assembly.GetExecutingAssembly());

    // Scan with filtering
    messaging.AddConsumersFromAssembly(Assembly.GetExecutingAssembly(), scan =>
    {
        scan.Where(type => !type.Name.EndsWith("Legacy"));
        scan.InNamespace("MyApp.MessageHandlers");
        scan.ExcludeType<DeprecatedHandler>();
    });
});

// Handlers discovered automatically
public sealed class OrderCreatedHandler : IConsume<OrderCreated> { ... }
public sealed class OrderShippedHandler : IConsume<OrderShipped> { ... }
// Each subscribes to topic matching their TMessage.Name
```

### Global Conventions (DRY Configuration)

```csharp
services.AddMessaging(messaging =>
{
    // Configure conventions once, apply to all
    messaging.ConfigureConventions(conventions =>
    {
        // Transform all topic names: OrderCreated → order-created
        conventions.UseKebabCaseTopics();

        // Or custom transformation
        conventions.TopicNamingConvention(type =>
            $"myapp.events.{type.Name.ToLowerInvariant()}");

        // Prefix all topics
        conventions.TopicPrefix("production.");

        // Default group for all consumers
        conventions.DefaultGroup("my-service");

        // Default concurrency
        conventions.DefaultConcurrency(3);
    });

    // Scan - all handlers use conventions
    messaging.AddConsumersFromAssembly(Assembly.GetExecutingAssembly());
    // OrderCreatedHandler → topic: "production.order-created", group: "my-service"
});
```

### Per-Handler Overrides

```csharp
services.AddMessaging(messaging =>
{
    messaging.ConfigureConventions(c => c.UseKebabCaseTopics());

    // Override convention for specific handler
    messaging.AddConsumer<OrderCreatedHandler>(c =>
    {
        c.Topic("orders.created");  // Override kebab-case convention
        c.Group("high-priority");    // Override default group
        c.Concurrency(10);           // Override default concurrency
    });

    // Another handler uses conventions
    messaging.AddConsumer<OrderShippedHandler>();
    // Uses kebab-case: "order-shipped"
});
```

### Message Type → Topic Mapping

```csharp
services.AddMessaging(messaging =>
{
    // Map message types to topics (affects all handlers for that type)
    messaging.MapTopic<OrderCreated>("orders.created");
    messaging.MapTopic<OrderShipped>("orders.shipped");
    messaging.MapTopic<PaymentReceived>("payments.received");

    // Now any handler for these types uses the mapped topics
    messaging.AddConsumersFromAssembly(Assembly.GetExecutingAssembly());
});
```

### Multiple Handlers per Message (Competing Consumers)

```csharp
services.AddMessaging(messaging =>
{
    // Same message type, different consumer groups
    messaging.AddConsumer<OrderCreatedHandler>(c => c.Group("order-processing"));
    messaging.AddConsumer<OrderAuditHandler>(c => c.Group("audit"));
    messaging.AddConsumer<OrderNotificationHandler>(c => c.Group("notifications"));

    // All subscribe to "OrderCreated" topic, different groups
    // Broker delivers message copy to each group
});
```

### Wildcard Topics & Multiple Topics

```csharp
services.AddMessaging(messaging =>
{
    // Subscribe to topic pattern (broker-dependent)
    messaging.AddConsumer<AllOrderEventsHandler>(c =>
    {
        c.TopicPattern("orders.*");  // Matches orders.created, orders.shipped, etc.
        c.Group("order-monitor");
    });

    // Subscribe to multiple specific topics
    messaging.AddConsumer<OrderAuditHandler>(c =>
    {
        c.Topics("orders.created", "orders.updated", "orders.deleted");
        c.Group("audit");
    });
});
```

### Filters & Middleware

```csharp
services.AddMessaging(messaging =>
{
    // Global filters (wraps CAP's SubscriberFilters)
    messaging.AddFilter<LoggingFilter>();    // Maps to cap.SubscriberFilters.Add<LoggingFilter>()
    messaging.AddFilter<MetricsFilter>();    // Maps to cap.SubscriberFilters.Add<MetricsFilter>()

    // NOTE: CAP's SubscriberFilters are global only
    // For per-consumer filters, implement inside the handler
    messaging.AddConsumer<PaymentHandler>();
});

// Filter implementation (uses CAP's IConsumeFilter interface)
public sealed class ValidationFilter : IConsumeFilter
{
    public async Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        // Pre-execution validation
        var message = context.Arguments[0];
        await ValidateAsync(message);
    }

    public Task OnSubscribeExecutedAsync(ExecutedContext context) => Task.CompletedTask;
    public Task OnSubscribeExceptionAsync(ExceptionContext context) => Task.CompletedTask;
}

// NOTE: Part 4 (Filters) should be simplified to:
// - Expose CAP's SubscriberFilters API (global filters only)
// - Remove custom FilterPipeline (CAP already has this)
// - Remove per-consumer filters (use composition in handlers instead)
```

### Retry & Error Handling

```csharp
services.AddMessaging(messaging =>
{
    // CAP retry configuration (wraps CAP's FailedRetryCount)
    messaging.ConfigureRetry(retry =>
    {
        retry.MaxRetries(3);  // Maps to cap.FailedRetryCount = 3
        retry.RetryInterval(TimeSpan.FromSeconds(60));  // Maps to cap.FailedRetryInterval
    });

    // Dead letter queue (wraps CAP's FailedThresholdCallback)
    messaging.ConfigureDeadLetterQueue(dlq =>
    {
        dlq.Topic("dead-letter");  // Send failed messages here
        dlq.AfterMaxRetries();     // Trigger after retry exhausted
    });

    // CAP handles the actual retry logic - we just expose configuration
});

// NOTE: Parts 3-5 of the sub-plans should be simplified to:
// - Expose CAP's retry configuration via our API (don't reimplement)
// - Expose CAP's DLQ via FailedThresholdCallback (don't reimplement)
// - Expose CAP's SubscriberFilters for filters (don't reimplement)
```

### Environment-Specific & Conditional Registration

```csharp
services.AddMessaging(messaging =>
{
    messaging.ConfigureConventions(c =>
    {
        c.TopicPrefix(env.IsProduction() ? "prod." : "dev.");
        c.DefaultGroup($"{env.EnvironmentName}.my-service");
    });

    // Always register
    messaging.AddConsumer<OrderCreatedHandler>();

    // Production only
    if (env.IsProduction())
    {
        messaging.AddConsumer<MetricsHandler>();
        messaging.AddConsumer<AlertingHandler>();
    }

    // Development only
    if (env.IsDevelopment())
    {
        messaging.AddConsumer<DebugHandler>(c => c.Concurrency(1));
    }
});
```

### Validation & Type Safety

```csharp
services.AddMessaging(messaging =>
{
    // Validate configuration at startup
    messaging.ValidateOnStartup();

    // Strict mode - fail fast on misconfiguration
    messaging.ConfigureValidation(validation =>
    {
        validation.RequireGroup();  // All consumers must have group
        validation.RequireExplicitTopic();  // No convention-based topics
        validation.ForbidDuplicateHandlers();  // No duplicate TMessage handlers
    });

    messaging.AddConsumer<OrderCreatedHandler>(c =>
    {
        // Compile-time error if OrderCreatedHandler doesn't implement IConsume<T>
        c.Topic("orders.created");
        c.Group("order-processing");  // Required by validation
    });
});
```

### Multiple Message Types Per Handler

```csharp
// Handler implementing multiple interfaces
public sealed class OrderEventHandler :
    IConsume<OrderCreated>,
    IConsume<OrderShipped>,
    IConsume<OrderCancelled>
{
    public async ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken ct)
        => await LogEventAsync("OrderCreated", context.Message, ct);

    public async ValueTask Consume(ConsumeContext<OrderShipped> context, CancellationToken ct)
        => await LogEventAsync("OrderShipped", context.Message, ct);

    public async ValueTask Consume(ConsumeContext<OrderCancelled> context, CancellationToken ct)
        => await LogEventAsync("OrderCancelled", context.Message, ct);
}

// Option 1: Explicit registration for each message type
services.AddMessaging(messaging =>
{
    messaging.AddConsumer<OrderEventHandler>(c =>
    {
        c.ForMessage<OrderCreated>().Topic("orders.created");
        c.ForMessage<OrderShipped>().Topic("orders.shipped");
        c.ForMessage<OrderCancelled>().Topic("orders.cancelled");
        c.Group("order-events");  // Shared group for all
    });
});

// Option 2: Assembly scanning (discovers all IConsume<T> automatically)
services.AddMessaging(messaging =>
{
    messaging.MapTopic<OrderCreated>("orders.created");
    messaging.MapTopic<OrderShipped>("orders.shipped");
    messaging.MapTopic<OrderCancelled>("orders.cancelled");
    messaging.AddConsumersFromAssembly(Assembly.GetExecutingAssembly());
    // OrderEventHandler subscribes to all three topics
});
```

### Complete Real-World Example

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMessaging(messaging =>
{
    // CAP transport configuration (REQUIRED - wraps AddCap())
    messaging.ConfigureCap(cap =>
    {
        cap.UseRabbitMQ(options =>
        {
            options.HostName = builder.Configuration["RabbitMQ:Host"];
            options.UserName = builder.Configuration["RabbitMQ:User"];
            options.Password = builder.Configuration["RabbitMQ:Password"];
        });

        cap.UseSqlServer(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("Default");
        });

        // CAP dashboard (optional)
        cap.UseDashboard();
    });

    // Global conventions
    messaging.ConfigureConventions(conventions =>
    {
        conventions.UseKebabCaseTopics();  // OrderCreated → order-created
        conventions.TopicPrefix($"{builder.Environment.EnvironmentName}.");
        conventions.DefaultGroup("order-service");
        conventions.DefaultConcurrency(3);
    });

    // Global filters (wraps CAP.SubscriberFilters)
    messaging.AddFilter<LoggingFilter>();
    messaging.AddFilter<MetricsFilter>();

    // Retry configuration (wraps CAP.FailedRetryCount)
    messaging.ConfigureRetry(retry =>
    {
        retry.MaxRetries(3);  // Maps to cap.FailedRetryCount = 3
        retry.RetryInterval(TimeSpan.FromSeconds(60));
    });

    // Dead letter queue (wraps CAP.FailedThresholdCallback)
    messaging.ConfigureDeadLetterQueue(dlq =>
    {
        dlq.Topic("dead-letter");
    });

    // Topic mappings (override conventions)
    messaging.MapTopic<OrderCreated>("orders.created");
    messaging.MapTopic<PaymentReceived>("payments.received");

    // Scan for handlers
    messaging.AddConsumersFromAssembly(typeof(Program).Assembly, scan =>
    {
        scan.InNamespace("OrderService.Handlers");
        scan.ExcludeType<DeprecatedHandler>();
    });

    // Critical handler with special config
    messaging.AddConsumer<PaymentHandler>(c =>
    {
        c.Topic("payments.received");
        c.Group("payment-processing");
        c.Concurrency(1);  // One at a time for payments
    });

    // Monitoring handler (production only)
    if (builder.Environment.IsProduction())
    {
        messaging.AddConsumer<MetricsCollectorHandler>(c =>
        {
            c.TopicPattern("*.events.*");  // All event topics
            c.Group("monitoring");
            c.Concurrency(10);
        });
    }
});

var app = builder.Build();
app.Run();

// NOTE: No separate services.AddCap() call - it's all unified in AddMessaging()
```

### API Design Summary

**Key DX Improvements:**

1. **No `.Build()`** - Registration happens on method call
2. **Lambda configuration** - Cleaner than chained methods
3. **Conventions API** - Configure once, apply everywhere
4. **Assembly scanning with filters** - Fine-grained control
5. **Global vs. per-consumer config** - Flexibility without repetition
6. **Type safety** - Compile-time validation where possible
7. **Retry & error handling** - First-class support
8. **Filters** - Global and per-consumer
9. **Validation** - Fail fast on misconfiguration
10. **Environment-aware** - Easy conditional registration

**Method Naming:**
- `AddMessaging()` - Main entry point (wraps AddCap internally)
- `ConfigureCap()` - CAP transport/storage configuration (replaces AddCap)
- `AddConsumer<T>()` - Register single handler
- `AddConsumersFromAssembly()` - Scan assembly
- `ConfigureConventions()` - Global defaults
- `MapTopic<T>()` - Message type → topic mapping
- `AddFilter<T>()` - Register filter (wraps CAP.SubscriberFilters)
- `ConfigureRetry()` - Retry configuration (wraps CAP.FailedRetryCount)
- `ConfigureDeadLetterQueue()` - DLQ config (wraps CAP.FailedThresholdCallback)
- `ValidateOnStartup()` - Validate config (optional, might skip)

### Handler Implementation Examples

```csharp
// Simple handler
public sealed class OrderCreatedHandler(IOrderService orderService)
    : IConsume<OrderCreated>
{
    public async ValueTask Consume(
        ConsumeContext<OrderCreated> context,
        CancellationToken cancellationToken)
    {
        await orderService.ProcessAsync(
            context.Message,
            context.CorrelationId,
            cancellationToken);
    }
}

// Handler with rich context usage
public sealed class OrderShippedHandler(
    ILogger<OrderShippedHandler> logger,
    INotificationService notifications)
    : IConsume<OrderShipped>
{
    public async ValueTask Consume(
        ConsumeContext<OrderShipped> context,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Processing order {OrderId} shipped at {ShippedAt}. MessageId: {MessageId}, CorrelationId: {CorrelationId}",
            context.Message.OrderId,
            context.Message.ShippedAt,
            context.MessageId,
            context.CorrelationId);

        // Access custom headers
        var userId = context.Headers["UserId"];
        var retryCount = context.Headers["RetryCount"];

        await notifications.SendShippingNotificationAsync(
            context.Message,
            userId,
            ct);
    }
}

// Handler implementing multiple message types
public sealed class OrderAuditHandler(IAuditService audit)
    : IConsume<OrderCreated>,
      IConsume<OrderShipped>,
      IConsume<OrderCancelled>
{
    public async ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken ct)
    {
        await audit.LogAsync("OrderCreated", context.Message, ct);
    }

    public async ValueTask Consume(ConsumeContext<OrderShipped> context, CancellationToken ct)
    {
        await audit.LogAsync("OrderShipped", context.Message, ct);
    }

    public async ValueTask Consume(ConsumeContext<OrderCancelled> context, CancellationToken ct)
    {
        await audit.LogAsync("OrderCancelled", context.Message, ct);
    }
}
```

### Test Helper

```csharp
// Test harness builder
public sealed class ConsumeContextBuilder<TMessage> where TMessage : class
{
    private TMessage _message = default!;
    private Guid _messageId = Guid.NewGuid();
    private Guid? _correlationId;
    private Dictionary<string, string?> _headers = [];
    private DateTimeOffset _timestamp = DateTimeOffset.UtcNow;
    private string _topic = typeof(TMessage).Name;

    public ConsumeContextBuilder<TMessage> WithMessage(TMessage message)
    {
        _message = message;
        return this;
    }

    public ConsumeContextBuilder<TMessage> WithCorrelationId(Guid correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public ConsumeContextBuilder<TMessage> WithHeader(string key, string? value)
    {
        _headers[key] = value;
        return this;
    }

    public ConsumeContext<TMessage> Build()
    {
        return new ConsumeContext<TMessage>
        {
            Message = _message,
            MessageId = _messageId,
            CorrelationId = _correlationId,
            Headers = new MessageHeader(_headers),
            Timestamp = _timestamp,
            Topic = _topic
        };
    }
}

// Usage in tests
[Fact]
public async Task should_process_order_when_message_received()
{
    // Arrange
    var orderService = Substitute.For<IOrderService>();
    var handler = new OrderCreatedHandler(orderService);
    var context = new ConsumeContextBuilder<OrderCreated>()
        .WithMessage(new OrderCreated { OrderId = 123 })
        .WithCorrelationId(Guid.NewGuid())
        .Build();

    // Act
    await handler.Consume(context, CancellationToken.None);

    // Assert
    await orderService.Received(1).ProcessAsync(
        Arg.Is<OrderCreated>(o => o.OrderId == 123),
        Arg.Any<Guid?>(),
        Arg.Any<CancellationToken>());
}
```

---

## Implementation Checklist

### Phase 1: Cleanup (Remove Old Pattern)

- [ ] Delete `src/Framework.Messages.Abstractions/IConsumer.cs`
- [ ] Delete attributes from `src/Framework.Messages.Abstractions/Attributes.cs` (`[CapSubscribe]`, `[FromCap]`, `[Topic]`)
- [ ] Remove reflection-based parameter binding from `ISubscribeInvoker.Default.cs`
- [ ] Remove old discovery logic from `IConsumerServiceSelector.Default.cs`
- [ ] Delete old tests: `ConsumerServiceSelectorTest.cs`, reflection-based tests
- [ ] Clean up demo files using old pattern (if any)

### Phase 2: Framework.Messages.Abstractions

- [ ] `MessagingBuilderExtensions.cs` - `AddMessaging()` extension method
- [ ] Update `IMessagingBuilder.cs` - Add methods:
  - `AddConsumer<TConsumer>(Action<IConsumerConfigurator<TConsumer>>? configure = null)`
  - `AddConsumersFromAssembly(Assembly, Action<IAssemblyScanner>? configure = null)`
  - `ConfigureConventions(Action<IConventionConfigurator>)`
  - `MapTopic<TMessage>(string topic)`
  - `AddFilter<TFilter>()`
  - `ConfigureRetryPolicy(Action<IRetryPolicyConfigurator>)`
  - `ConfigureDeadLetterQueue(Action<IDeadLetterQueueConfigurator>)`
  - `ConfigureValidation(Action<IValidationConfigurator>)`
  - `ValidateOnStartup()`
- [ ] Create `IConsumerConfigurator<T>` - Handler-specific config:
  - `Topic(string topic)`
  - `Topics(params string[] topics)`
  - `TopicPattern(string pattern)`
  - `Group(string group)`
  - `Concurrency(int count)`
  - `AddFilter<TFilter>()`
  - `RetryPolicy(Action<IRetryPolicyConfigurator>)`
  - `ForMessage<TMessage>()` (for multi-message handlers)
- [ ] Create `IConventionConfigurator` - Global conventions:
  - `UseKebabCaseTopics()`
  - `TopicNamingConvention(Func<Type, string> convention)`
  - `TopicPrefix(string prefix)`
  - `DefaultGroup(string group)`
  - `DefaultConcurrency(int count)`
- [ ] Create `IAssemblyScanner` - Assembly scan filters:
  - `Where(Func<Type, bool> predicate)`
  - `InNamespace(string namespace)`
  - `ExcludeType<T>()`
- [ ] Create `IRetryPolicyConfigurator` - Retry config:
  - `MaxRetries(int count)`
  - `BackoffExponential(TimeSpan initialDelay)`
  - `BackoffLinear(TimeSpan delay)`
  - `RetryOn<TException>()`
  - `DoNotRetryOn<TException>()`
- [ ] Create `IDeadLetterQueueConfigurator` - DLQ config:
  - `Topic(string topic)`
  - `AfterMaxRetries()`
  - `IncludeOriginalMessage()`
  - `IncludeExceptionDetails()`
- [ ] Create `IValidationConfigurator` - Validation rules:
  - `RequireGroup()`
  - `RequireExplicitTopic()`
  - `ForbidDuplicateHandlers()`
- [ ] XML docs for all public APIs

### Phase 3: Framework.Messages.Core

- [ ] `MessagingBuilder.cs` - Implement `IMessagingBuilder`
- [ ] `ConsumerConfigurator.cs` - Implement `IConsumerConfigurator<T>`
- [ ] `ConventionConfigurator.cs` - Implement `IConventionConfigurator`
- [ ] `AssemblyScanner.cs` - Implement `IAssemblyScanner`
- [ ] `RetryPolicyConfigurator.cs` - Implement `IRetryPolicyConfigurator`
- [ ] `DeadLetterQueueConfigurator.cs` - Implement `IDeadLetterQueueConfigurator`
- [ ] `ValidationConfigurator.cs` - Implement `IValidationConfigurator`
- [ ] `ConsumerRegistry.cs` - Store TMessage → ConsumerMetadata mappings
- [ ] `TopicNamingStrategy.cs` - Apply conventions to message types
- [ ] `StringExtensions.cs` - `.ToKebabCase()` for topic naming
- [ ] Rewrite `IConsumerServiceSelector.Default.cs` - Only find `IConsume<T>`
- [ ] Simplify `ISubscribeInvoker.Default.cs` - Only use `CompiledMessageDispatcher`
- [ ] `ConsumeContextFactory.cs` - Build `ConsumeContext<T>` from `MediumMessage`
- [ ] Update filter integration for `IConsumeFilter`
- [ ] `ConfigurationValidator.cs` - Validate builder config at startup

### Phase 4: Tests

- [ ] `MessagingBuilderTest.cs` - Builder API unit tests
- [ ] `ConsumerRegistryTest.cs` - Registry unit tests
- [ ] `SimplifiedConsumerServiceSelectorTest.cs` - Discovery tests for `IConsume<T>`
- [ ] `IConsumeIntegrationTest.cs` - End-to-end message flow
- [ ] `ConsumeContextBuilderTest.cs` - Test harness validation
- [ ] Performance benchmarks (old reflection vs. new compiled)

### Phase 5: Documentation

- [ ] `src/Framework.Messages.Abstractions/README.md` - Document new pattern
- [ ] `src/Framework.Messages.Core/README.md` - Updated architecture docs
- [ ] Code examples in XML docs
- [ ] Inline comments for complex logic

### Phase 6: Validation

- [ ] Run `./build.sh CoverageAnalysis --test-project Framework.Messages.Core.Tests.Unit`
- [ ] Line coverage ≥85%, branch coverage ≥80%
- [ ] Run `dotnet csharpier .` to format code
- [ ] All new `IConsume<T>` integration tests pass
- [ ] Performance benchmark shows ≥5x improvement over old reflection
