# Complete IConsume<T> Messaging API

## Overview

Complete the type-safe `IConsume<T>` messaging API by finishing the core integration, adding convention-based configuration, and providing optional CAP wrappers for advanced features.

**Current Status**: ~75% complete
- ✅ Core infrastructure (registry, metadata, builder)
- ✅ Unified API (`MessagingOptions : IMessagingBuilder`)
- ✅ Old pattern removed (`[CapSubscribe]`)
- 🔄 Registry integration with selector/invoker
- ⏸️ Conventions & scanning
- ⏸️ CAP wrappers (retry, filters, validation)

**Target**: Ship fully functional type-safe messaging system with excellent DX

---

## Current API

```csharp
services.AddMessages(options =>
{
    // Infrastructure configuration
    options.FailedRetryCount = 50;
    options.UseSqlServer("connection_string");
    options.UseRabbitMQ(rabbit =>
    {
        rabbit.HostName = "localhost";
        rabbit.Port = 5672;
    });

    // Consumer registration - MANUAL (Part 1 MVP)
    options.Consumer<OrderPlacedHandler>()
        .Topic("orders.placed")
        .Group("order-service")
        .WithConcurrency(5)
        .Build();

    // Consumer registration - SCANNING (Part 2 - not yet implemented)
    // options.ScanConsumers(typeof(Program).Assembly);

    // Topic mapping for type-safe publishing
    options.WithTopicMapping<OrderPlaced>("orders.placed");
});
```

---

## Problem Statement

### **What's Working** ✅
- Type-safe `IConsume<T>` interface
- Rich `ConsumeContext<T>` with message metadata
- `ConsumerRegistry` with freeze-on-read pattern
- Fluent `ConsumerBuilder<T>` API
- Unified configuration object

### **What's Missing** 🔄
1. **Core Integration**: Registry not connected to message routing pipeline
2. **Convention-based Discovery**: Must manually register each consumer
3. **Developer Experience**: No topic naming conventions, assembly scanning
4. **Advanced Features**: No simplified retry/filter/validation wrappers

### **Impact**
- **Developers**: Manual registration is tedious for large projects
- **Performance**: Not using compiled dispatcher (still reflection-based)
- **Migration**: Can't leverage existing patterns (assembly scanning)

---

## Proposed Solution

### **Phase 1: MVP - Core Integration** (1-2 days) 🎯
Complete the foundation by connecting `ConsumerRegistry` to the message routing pipeline and enabling compiled dispatch.

**Deliverables**:
- Registry-driven consumer selection
- Compiled message dispatcher integration
- End-to-end message flow validation
- Performance benchmarks

### **Phase 2: Conventions & Scanning** (2 days) 🚀
Add developer-friendly features for automatic discovery and topic naming.

**Deliverables**:
- Convention-based topic naming (kebab-case, Pascal-case)
- Assembly scanning with filters
- Global prefix/suffix configuration
- Topic mapping helpers

### **Phase 3: Optional CAP Wrappers** (~1 day) 🎁
Provide simplified APIs for retry, filters, and validation by wrapping CAP's existing features.

**Deliverables**:
- Retry configuration wrapper
- Filter registration wrapper
- Minimal duplicate detection

---

## Technical Approach

### **Phase 1: Core Integration**

#### **1.1 Update IConsumerServiceSelector** (src/Framework.Messages.Core/Internal/IConsumerServiceSelector.Default.cs)

**Current**: Uses reflection to scan for `[CapSubscribe]` attributes
**Target**: Use `ConsumerRegistry` to find registered consumers

```csharp
public class ConsumerServiceSelector : IConsumerServiceSelector
{
    private readonly ConsumerRegistry _registry;

    public ConsumerServiceSelector(ConsumerRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<ConsumerExecutorDescriptor> SelectCandidates()
    {
        var allConsumers = _registry.GetAll();

        return allConsumers.Select(metadata => new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = metadata.ConsumerType.GetTypeInfo(),
            ImplTypeInfo = metadata.ConsumerType.GetTypeInfo(),
            Attribute = new CapSubscribeAttribute(metadata.Topic)
            {
                Group = metadata.Group
            },
            TopicName = metadata.Topic,
            MethodInfo = metadata.ConsumerType.GetMethod("ConsumeAsync")!,
            Parameters = GetParameters(metadata)
        }).ToList();
    }

    private static IList<ParameterDescriptor> GetParameters(ConsumerMetadata metadata)
    {
        // Build parameter descriptors for ConsumeAsync(ConsumeContext<TMessage>, CancellationToken)
        var contextType = typeof(ConsumeContext<>).MakeGenericType(metadata.MessageType);

        return new List<ParameterDescriptor>
        {
            new()
            {
                Name = "context",
                ParameterType = contextType,
                IsFromCap = false
            },
            new()
            {
                Name = "cancellationToken",
                ParameterType = typeof(CancellationToken),
                IsFromCap = false
            }
        };
    }
}
```

**Files**:
- `src/Framework.Messages.Core/Internal/IConsumerServiceSelector.Default.cs`

**Tests**:
- `tests/Framework.Messages.Core.Tests.Unit/ConsumerServiceSelectorTests.cs` (update existing)

---

#### **1.2 Update ISubscribeInvoker** (src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs)

**Current**: Uses `ObjectMethodExecutor` (reflection-based)
**Target**: Use `CompiledMessageDispatcher.DispatchAsync()` (5-10x faster)

```csharp
public class SubscribeInvoker : ISubscribeInvoker
{
    private readonly IMessageDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;

    public async Task<ConsumerExecutedResult> InvokeAsync(
        ConsumerContext context,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var metadata = _registry.FindByTopic(context.ConsumerDescriptor.TopicName);

        // Build ConsumeContext<TMessage>
        var consumeContext = BuildConsumeContext(metadata, context, scope);

        // Dispatch using compiled dispatcher (5-10x faster)
        await _dispatcher.DispatchAsync(
            consumeContext,
            metadata.ConsumerType,
            metadata.MessageType,
            cancellationToken
        );

        return ConsumerExecutedResult.Success;
    }

    private object BuildConsumeContext(
        ConsumerMetadata metadata,
        ConsumerContext context,
        IServiceScope scope)
    {
        var messageType = metadata.MessageType;
        var message = DeserializeMessage(context.Message.Value, messageType);

        var contextType = typeof(ConsumeContext<>).MakeGenericType(messageType);

        return Activator.CreateInstance(
            contextType,
            message,
            context.Message.Headers,
            context.Message.GetId(),
            context.DeliverMessage.Topic,
            context.DeliverMessage.Group,
            scope.ServiceProvider
        )!;
    }
}
```

**Files**:
- `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs`

**Tests**:
- `tests/Framework.Messages.Core.Tests.Unit/SubscribeInvokerTests.cs` (update existing)

---

#### **1.3 End-to-End Integration Tests**

**Test Scenarios**:
1. Publish message → verify consumer receives via `IConsume<T>.ConsumeAsync()`
2. Multi-message handler (implements multiple `IConsume<T>`)
3. DI scope isolation (each message gets new scope)
4. Concurrency limits respected

**Files**:
- `tests/Framework.Messages.Core.Tests.Integration/IConsumeIntegrationTests.cs` (new)

**Example**:
```csharp
public sealed class IConsumeIntegrationTests : TestBase
{
    [Fact]
    public async Task should_invoke_consumer_when_message_published()
    {
        // Arrange
        var received = new TaskCompletionSource<OrderPlaced>();

        var services = new ServiceCollection();
        services.AddMessages(options =>
        {
            options.UseInMemoryStorage();
            options.UseInMemoryQueue();
            options.Consumer<OrderPlacedHandler>()
                .Topic("orders.placed")
                .Build();
        });
        services.AddScoped<OrderPlacedHandler>(sp =>
            new OrderPlacedHandler(received));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IOutboxPublisher>();

        // Act
        await publisher.PublishAsync(new OrderPlaced { OrderId = 123 });

        // Assert
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        message.OrderId.Should().Be(123);
    }
}

public sealed class OrderPlacedHandler : IConsume<OrderPlaced>
{
    private readonly TaskCompletionSource<OrderPlaced> _received;

    public OrderPlacedHandler(TaskCompletionSource<OrderPlaced> received)
    {
        _received = received;
    }

    public async Task ConsumeAsync(
        ConsumeContext<OrderPlaced> context,
        CancellationToken cancellationToken)
    {
        _received.SetResult(context.Message);
        await Task.CompletedTask;
    }
}
```

---

#### **1.4 Performance Benchmarks**

**Goal**: Verify 5-10x speedup from compiled dispatcher

**Benchmark**:
```csharp
[MemoryDiagnoser]
public class DispatcherBenchmarks
{
    private IMessageDispatcher _compiledDispatcher;
    private ObjectMethodExecutor _reflectionExecutor;

    [Benchmark(Baseline = true)]
    public async Task Reflection_Dispatch()
    {
        await _reflectionExecutor.ExecuteAsync(handler, new object[] { context, cts.Token });
    }

    [Benchmark]
    public async Task Compiled_Dispatch()
    {
        await _compiledDispatcher.DispatchAsync(context, typeof(Handler), typeof(Message), cts.Token);
    }
}
```

**Expected Results**:
| Method              | Mean     | Allocated |
|---------------------|----------|-----------|
| Reflection_Dispatch | 500 ns   | 240 B     |
| Compiled_Dispatch   | 50 ns    | 32 B      |

**Files**:
- `benchmarks/Framework.Messages.Benchmarks/DispatcherBenchmarks.cs` (new)

---

#### **1.5 Code Coverage**

**Target**: ≥85% line coverage, ≥80% branch coverage

**Command**:
```bash
./build.sh CoverageAnalysis --test-project Framework.Messages.Core.Tests.Unit
```

**Critical paths to cover**:
- Consumer registration (manual + scanning)
- Topic mapping and resolution
- Message dispatch (compiled path)
- DI scope management
- Error handling

---

### **Phase 2: Conventions & Scanning**

#### **2.1 Convention-Based Topic Naming**

**Goal**: Reduce boilerplate by auto-generating topic names from message types

**API**:
```csharp
services.AddMessages(options =>
{
    options.ConfigureConventions(c =>
    {
        c.UseKebabCaseTopics();  // OrderCreated → order-created
        c.TopicPrefix("prod.");  // → prod.order-created
        c.TopicSuffix(".v1");    // → prod.order-created.v1
        c.DefaultGroup("my-service");
    });
});
```

**Implementation**:
```csharp
// src/Framework.Messages.Core/Configuration/MessagingConventions.cs
public sealed class MessagingConventions
{
    public TopicNamingConvention TopicNaming { get; set; } = TopicNamingConvention.TypeName;
    public string? TopicPrefix { get; set; }
    public string? TopicSuffix { get; set; }
    public string? DefaultGroup { get; set; }

    public string GetTopicName(Type messageType)
    {
        var baseName = TopicNaming switch
        {
            TopicNamingConvention.KebabCase => messageType.Name.ToKebabCase(),
            TopicNamingConvention.PascalCase => messageType.Name,
            TopicNamingConvention.TypeName => messageType.Name,
            _ => messageType.Name
        };

        return $"{TopicPrefix}{baseName}{TopicSuffix}";
    }
}

public enum TopicNamingConvention
{
    TypeName,    // OrderCreated
    KebabCase,   // order-created
    PascalCase   // OrderCreated
}

// src/Framework.Base/Extensions/StringExtensions.cs
public static string ToKebabCase(this string value)
{
    if (string.IsNullOrEmpty(value))
        return value;

    return Regex.Replace(
        value,
        "(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])",
        "-$1",
        RegexOptions.Compiled
    ).ToLower();
}
```

**Files**:
- `src/Framework.Messages.Core/Configuration/MessagingConventions.cs` (new)
- `src/Framework.Base/Extensions/StringExtensions.cs` (add ToKebabCase)

**Tests**:
- `tests/Framework.Messages.Core.Tests.Unit/MessagingConventionsTests.cs` (new)
- `tests/Framework.Base.Tests.Unit/StringExtensionsTests.cs` (update)

---

#### **2.2 Assembly Scanning**

**Goal**: Auto-discover all `IConsume<T>` implementations in assembly

**API**:
```csharp
services.AddMessages(options =>
{
    // Scan entire assembly
    options.ScanConsumers(typeof(Program).Assembly);

    // Scan with filters
    options.ScanConsumers(typeof(Program).Assembly, scan =>
    {
        scan.InNamespace("MyApp.Handlers");
        scan.Where(t => t.Name.EndsWith("Handler"));
        scan.ExcludeType<BaseHandler>();
    });
});
```

**Implementation** (already exists in MessagingOptions):
```csharp
// src/Framework.Messages.Core/Configuration/MessagingOptions.cs
public IMessagingBuilder ScanConsumers(Assembly assembly)
{
    Argument.IsNotNull(assembly);

    var consumerTypes = assembly
        .GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
        .Where(t => t.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>)))
        .ToList();

    foreach (var consumerType in consumerTypes)
    {
        var consumeInterfaces = consumerType
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
            .ToList();

        foreach (var consumeInterface in consumeInterfaces)
        {
            var messageType = consumeInterface.GetGenericArguments()[0];
            var topic = _conventions?.GetTopicName(messageType) ?? messageType.Name;

            RegisterConsumer(consumerType, messageType, topic, group: null, concurrency: 1);
        }
    }

    return this;
}
```

**Enhancement**: Add filtering support
```csharp
// src/Framework.Messages.Core/AssemblyScanner.cs (new)
public sealed class AssemblyScanner
{
    private readonly List<Func<Type, bool>> _filters = new();
    private string? _namespaceFilter;
    private readonly HashSet<Type> _excludedTypes = new();

    public AssemblyScanner InNamespace(string ns)
    {
        _namespaceFilter = ns;
        return this;
    }

    public AssemblyScanner Where(Func<Type, bool> predicate)
    {
        _filters.Add(predicate);
        return this;
    }

    public AssemblyScanner ExcludeType<T>()
    {
        _excludedTypes.Add(typeof(T));
        return this;
    }

    internal IEnumerable<Type> Scan(Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>)));

        if (_namespaceFilter != null)
            types = types.Where(t => t.Namespace?.StartsWith(_namespaceFilter) == true);

        foreach (var filter in _filters)
            types = types.Where(filter);

        types = types.Where(t => !_excludedTypes.Contains(t));

        return types;
    }
}

// Update MessagingOptions
public IMessagingBuilder ScanConsumers(
    Assembly assembly,
    Action<AssemblyScanner>? configure = null)
{
    var scanner = new AssemblyScanner();
    configure?.Invoke(scanner);

    var consumerTypes = scanner.Scan(assembly);

    // Register each consumer...
}
```

**Files**:
- `src/Framework.Messages.Core/AssemblyScanner.cs` (new)
- `src/Framework.Messages.Core/Configuration/MessagingOptions.cs` (update ScanConsumers)

**Tests**:
- `tests/Framework.Messages.Core.Tests.Unit/AssemblyScannerTests.cs` (new)

---

#### **2.3 Topic Mapping Helpers**

**Goal**: Simplify topic mapping for type-safe publishing

**API**:
```csharp
services.AddMessages(options =>
{
    // Explicit mapping
    options.MapTopic<OrderCreated>("orders.created");

    // Batch mapping
    options.MapTopics(map =>
    {
        map.For<OrderCreated>("orders.created");
        map.For<OrderCancelled>("orders.cancelled");
    });

    // Convention-based (auto-map all messages in namespace)
    options.MapTopicsInNamespace("MyApp.Messages", "myapp.");
});
```

**Implementation**:
```csharp
// src/Framework.Messages.Core/Configuration/MessagingOptions.cs
public IMessagingBuilder MapTopic<TMessage>(string topic)
    where TMessage : class
{
    return WithTopicMapping<TMessage>(topic);
}

public IMessagingBuilder MapTopics(Action<TopicMapper> configure)
{
    var mapper = new TopicMapper(this);
    configure(mapper);
    return this;
}

public IMessagingBuilder MapTopicsInNamespace(string ns, string topicPrefix)
{
    var assembly = Assembly.GetCallingAssembly();
    var messageTypes = assembly.GetTypes()
        .Where(t => t.Namespace?.StartsWith(ns) == true)
        .Where(t => t.IsClass && !t.IsAbstract);

    foreach (var type in messageTypes)
    {
        var topic = topicPrefix + type.Name.ToKebabCase();
        _WithTopicMapping(type, topic);
    }

    return this;
}

// src/Framework.Messages.Core/TopicMapper.cs (new)
public sealed class TopicMapper
{
    private readonly MessagingOptions _options;

    internal TopicMapper(MessagingOptions options)
    {
        _options = options;
    }

    public TopicMapper For<TMessage>(string topic)
        where TMessage : class
    {
        _options.WithTopicMapping<TMessage>(topic);
        return this;
    }
}
```

**Files**:
- `src/Framework.Messages.Core/TopicMapper.cs` (new)
- `src/Framework.Messages.Core/Configuration/MessagingOptions.cs` (add helpers)

**Tests**:
- `tests/Framework.Messages.Core.Tests.Unit/TopicMappingTests.cs` (new)

---

### **Phase 3: Optional CAP Wrappers**

#### **3.1 Retry Configuration Wrapper**

**Goal**: Simplify retry config by wrapping CAP's existing retry logic

**API**:
```csharp
services.AddMessages(options =>
{
    options.ConfigureRetry(retry =>
    {
        retry.MaxRetries(3);  // → CapOptions.FailedRetryCount = 3
        retry.RetryInterval(TimeSpan.FromSeconds(60));  // → CapOptions.FailedRetryInterval = 60
    });
});
```

**Implementation**:
```csharp
// src/Framework.Messages.Core/Configuration/RetryConfigurator.cs (new)
public sealed class RetryConfigurator
{
    private readonly MessagingOptions _options;

    internal RetryConfigurator(MessagingOptions options)
    {
        _options = options;
    }

    public RetryConfigurator MaxRetries(int count)
    {
        _options.FailedRetryCount = count;
        return this;
    }

    public RetryConfigurator RetryInterval(TimeSpan interval)
    {
        _options.FailedRetryInterval = (int)interval.TotalSeconds;
        return this;
    }
}

// Update MessagingOptions
public IMessagingBuilder ConfigureRetry(Action<RetryConfigurator> configure)
{
    var configurator = new RetryConfigurator(this);
    configure(configurator);
    return this;
}
```

**Files**:
- `src/Framework.Messages.Core/Configuration/RetryConfigurator.cs` (new)

**Tests**:
- `tests/Framework.Messages.Core.Tests.Unit/RetryConfiguratorTests.cs` (new)

---

#### **3.2 Filter Registration Wrapper**

**Goal**: Simplify filter registration by wrapping CAP's `SubscribeFilters`

**API**:
```csharp
services.AddMessages(options =>
{
    options.AddFilter<LoggingFilter>();
    options.AddFilter<MetricsFilter>();
});
```

**Implementation**:
```csharp
// Already exposed via MessagingBuilder
// src/Framework.Messages.Core/Setup.cs
public static class Setup
{
    private static MessagingBuilder _RegisterCoreMessagingServices(...)
    {
        // ...existing code...

        return new MessagingBuilder(services);
    }
}

// src/Framework.Messages.Core/MessagingBuilder.cs
public class MessagingBuilder
{
    private readonly IServiceCollection _services;

    public MessagingBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public MessagingBuilder AddFilter<TFilter>()
        where TFilter : class, ISubscribeFilter
    {
        // Map to CAP's filter registration
        _services.Configure<MessagingOptions>(options =>
        {
            // Note: Need to store filters and apply them in _RegisterCoreMessagingServices
            // after CAP is configured
        });

        return this;
    }
}
```

**Note**: This is already partially implemented via `MessagingBuilder`. Just needs documentation.

---

#### **3.3 Minimal Validation**

**Goal**: Detect duplicate consumer registrations

**Implementation**:
```csharp
// src/Framework.Messages.Core/Configuration/MessagingOptions.cs
internal void FinalizeConfiguration()
{
    var allConsumers = Registry!.GetAll().ToList();

    // Warn if no consumers
    if (!allConsumers.Any())
    {
        // Log warning (need ILogger injection)
    }

    // Detect duplicates (same type + message + group)
    var duplicates = allConsumers
        .GroupBy(c => new { c.MessageType, c.ConsumerType, c.Group })
        .Where(g => g.Count() > 1)
        .ToList();

    if (duplicates.Any())
    {
        var duplicateInfo = string.Join(", ", duplicates.Select(g =>
            $"{g.Key.ConsumerType.Name} for {g.Key.MessageType.Name} in group '{g.Key.Group ?? "default"}'"));

        throw new InvalidOperationException(
            $"Duplicate consumer registrations detected: {duplicateInfo}");
    }
}

// Call from Setup.cs after configure(options)
services.AddMessages(options =>
{
    configure(options);
    options.FinalizeConfiguration();  // Validate before registering services
});
```

**Files**:
- `src/Framework.Messages.Core/Configuration/MessagingOptions.cs` (add FinalizeConfiguration)
- `src/Framework.Messages.Core/Setup.cs` (call FinalizeConfiguration)

**Tests**:
- `tests/Framework.Messages.Core.Tests.Unit/ValidationTests.cs` (new)

---

## Acceptance Criteria

### **Phase 1: MVP - Core Integration** ✅

#### Functional
- [ ] `IConsumerServiceSelector` uses `ConsumerRegistry` instead of reflection
- [ ] `ISubscribeInvoker` uses `CompiledMessageDispatcher` instead of `ObjectMethodExecutor`
- [ ] End-to-end message flow works: publish → broker → `IConsume<T>.ConsumeAsync()`
- [ ] Multi-message handlers supported (class implements multiple `IConsume<T>`)
- [ ] DI scope management correct (new scope per message)
- [ ] Concurrency limits respected

#### Quality
- [ ] Unit tests pass for selector, invoker, dispatcher
- [ ] Integration tests validate end-to-end flow
- [ ] Performance benchmarks show 5-10x improvement (compiled vs reflection)
- [ ] Code coverage ≥85% line, ≥80% branch

#### Documentation
- [ ] XML docs complete for all public APIs
- [ ] README updated with examples
- [ ] Migration guide written (`[CapSubscribe]` → `IConsume<T>`)

---

### **Phase 2: Conventions & Scanning** ✅

#### Functional
- [ ] Convention-based topic naming (kebab-case, Pascal-case)
- [ ] Global topic prefix/suffix configuration
- [ ] Assembly scanning with no filters
- [ ] Assembly scanning with namespace filter
- [ ] Assembly scanning with predicate filter
- [ ] Assembly scanning with type exclusions
- [ ] Topic mapping helpers (single, batch, namespace)

#### Quality
- [ ] Unit tests for conventions, scanner, mapping
- [ ] Integration tests for scanned consumers
- [ ] Code coverage maintained ≥85%

#### Documentation
- [ ] Convention examples in README
- [ ] Scanning examples in README
- [ ] Best practices guide

---

### **Phase 3: Optional CAP Wrappers** ✅

#### Functional
- [ ] Retry configuration wrapper (max retries, interval)
- [ ] Filter registration (via `MessagingBuilder`)
- [ ] Duplicate consumer detection (throws exception)
- [ ] Empty configuration warning (logs warning)

#### Quality
- [ ] Unit tests for retry, filters, validation
- [ ] Code coverage maintained ≥85%

#### Documentation
- [ ] Retry examples in README
- [ ] Filter examples in README

---

## Success Metrics

### **Performance**
- **Throughput**: ≥10,000 messages/sec per consumer
- **Latency**: p50 < 5ms, p99 < 20ms
- **Dispatch Speed**: 5-10x faster than reflection (compiled dispatcher)

### **Developer Experience**
- **Registration Time**: < 5 lines of code to register a consumer
- **Discoverability**: IntelliSense shows all `IConsume<T>` methods
- **Error Messages**: Clear, actionable errors for misconfigurations

### **Code Quality**
- **Coverage**: ≥85% line, ≥80% branch
- **Complexity**: No methods > 20 lines (except generated code)
- **Maintainability**: All public APIs have XML docs

---

## Dependencies & Risks

### **Dependencies**
- `Framework.Checks` - Argument validation
- `Framework.Base` - String extensions (ToKebabCase)
- `DotNetCore.CAP` - Underlying message infrastructure
- `Microsoft.Extensions.DependencyInjection` - DI container

### **Risks**

| Risk | Impact | Mitigation |
|------|--------|------------|
| Breaking changes to CAP API | HIGH | Pin CAP version, test with each upgrade |
| Performance regression | MEDIUM | Benchmark before/after, fail CI if < 5x |
| DI scope leaks | HIGH | Integration tests with scope validation |
| Multi-message handler bugs | MEDIUM | Unit + integration tests for this scenario |
| Convention conflicts | LOW | Allow explicit overrides, document precedence |

---

## Implementation Plan

### **Week 1: Phase 1 (MVP)** 🎯
**Days 1-2**: Core integration
- Update `IConsumerServiceSelector`
- Update `ISubscribeInvoker`
- Integration tests

**Day 3**: Validation & performance
- End-to-end testing
- Performance benchmarks
- Code coverage check

**Day 4**: Documentation
- XML docs
- README updates
- Migration guide

**Deliverable**: Fully functional type-safe messaging with compiled dispatch

---

### **Week 2: Phase 2 (Conventions)** 🚀
**Days 1-2**: Conventions & scanning
- `MessagingConventions` class
- `ToKebabCase()` extension
- Assembly scanner enhancements
- Topic mapping helpers

**Day 3**: Testing & refinement
- Unit tests
- Integration tests
- Coverage verification

**Deliverable**: Developer-friendly auto-discovery and conventions

---

### **Week 3: Phase 3 (Optional)** 🎁
**Day 1**: CAP wrappers
- Retry configurator
- Filter wrapper (documentation)
- Validation logic

**Deliverable**: Complete feature set with simplified APIs

---

## Files

### **Phase 1: Core Integration**
**Modified**:
- `src/Framework.Messages.Core/Internal/IConsumerServiceSelector.Default.cs`
- `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs`
- `tests/Framework.Messages.Core.Tests.Unit/ConsumerServiceSelectorTests.cs`
- `tests/Framework.Messages.Core.Tests.Unit/SubscribeInvokerTests.cs`

**New**:
- `tests/Framework.Messages.Core.Tests.Integration/IConsumeIntegrationTests.cs`
- `benchmarks/Framework.Messages.Benchmarks/DispatcherBenchmarks.cs`
- `docs/migration-guides/capsubscribe-to-iconsume.md`

---

### **Phase 2: Conventions & Scanning**
**Modified**:
- `src/Framework.Messages.Core/Configuration/MessagingOptions.cs`
- `src/Framework.Base/Extensions/StringExtensions.cs`
- `tests/Framework.Base.Tests.Unit/StringExtensionsTests.cs`

**New**:
- `src/Framework.Messages.Core/Configuration/MessagingConventions.cs`
- `src/Framework.Messages.Core/AssemblyScanner.cs`
- `src/Framework.Messages.Core/TopicMapper.cs`
- `tests/Framework.Messages.Core.Tests.Unit/MessagingConventionsTests.cs`
- `tests/Framework.Messages.Core.Tests.Unit/AssemblyScannerTests.cs`
- `tests/Framework.Messages.Core.Tests.Unit/TopicMappingTests.cs`

---

### **Phase 3: CAP Wrappers**
**Modified**:
- `src/Framework.Messages.Core/Configuration/MessagingOptions.cs`
- `src/Framework.Messages.Core/Setup.cs`

**New**:
- `src/Framework.Messages.Core/Configuration/RetryConfigurator.cs`
- `tests/Framework.Messages.Core.Tests.Unit/RetryConfiguratorTests.cs`
- `tests/Framework.Messages.Core.Tests.Unit/ValidationTests.cs`

---

## Future Considerations

### **Post-MVP Enhancements**
- **Saga support**: Long-running workflows with compensation
- **Outbox pattern**: Transactional message publishing
- **Distributed tracing**: OpenTelemetry integration
- **Message versioning**: Support for schema evolution
- **Dead letter queue**: Enhanced DLQ with retry policies per message type

### **Ticker Integration**
- Unify `Ticker` (scheduled/recurring messages) with `Messages`
- Single dashboard for all message types
- See future plan: `refactor-unify-ticker-messages-dx.md`

---

## References

### **Internal**
- `src/Framework.Messages.Core/ConsumerRegistry.cs` - Central registry
- `src/Framework.Messages.Core/ConsumerMetadata.cs` - Metadata model
- `src/Framework.Messages.Core/ConsumerBuilder.cs` - Fluent API
- `src/Framework.Messages.Core/Configuration/MessagingOptions.cs` - Unified config

### **External**
- [CAP Documentation](https://cap.dotnetcore.xyz/) - Underlying infrastructure
- [MediatR](https://github.com/jbogard/MediatR) - Similar dispatch pattern
- [MassTransit](https://masstransit.io/) - Convention inspiration

### **Related Work**
- Commit `1c4f3b4e` - Renamed CAP terminology to Messaging
- Commit `f3701a65` - Added ConsumerRegistry tests
- Commit `c414b724` - Migrated to IConsume<T> pattern
- Commit `407c4029` - Added ConsumeContext and IConsume interface

---

## Migration Path

### **From `[CapSubscribe]` to `IConsume<T>`**

**Before**:
```csharp
public class OrderHandler : ICapSubscribe
{
    [CapSubscribe("orders.created")]
    public async Task Handle(OrderCreated message)
    {
        // Process message
    }
}
```

**After**:
```csharp
public sealed class OrderHandler : IConsume<OrderCreated>
{
    public async Task ConsumeAsync(
        ConsumeContext<OrderCreated> context,
        CancellationToken cancellationToken)
    {
        var message = context.Message;
        // Process message
    }
}

// Registration
services.AddMessages(options =>
{
    options.ScanConsumers(typeof(Program).Assembly);
    // or
    options.Consumer<OrderHandler>()
        .Topic("orders.created")
        .Build();
});
```

### **Key Changes**
1. Implement `IConsume<TMessage>` instead of `ICapSubscribe`
2. Method signature: `ConsumeAsync(ConsumeContext<T>, CancellationToken)`
3. Remove `[CapSubscribe]` attribute
4. Register via `AddMessages()`
5. Access metadata via `context` parameter

### **Benefits**
- ✅ Compile-time type checking
- ✅ Better IDE support (navigation, refactoring)
- ✅ 5-10x faster dispatch (compiled expressions)
- ✅ Richer context (headers, correlation ID, DI scope)
- ✅ Testable (mock `ConsumeContext<T>`)
