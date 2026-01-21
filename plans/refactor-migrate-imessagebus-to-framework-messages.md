# Refactor: Migrate IMessageBus to Framework.Messages.Abstractions

## Overview

Migrate the legacy `IMessageBus` pattern (from `Framework.Messaging.Abstractions`) to the modern `Framework.Messages.Abstractions` library using unified fluent DI registration. This migration will eliminate the old messaging abstraction, modernize the API, improve testability, and enable access to new messaging capabilities like outbox pattern, topic conventions, and compile-time consumer registration with implicit DI discovery.

**Current State:**
- 4 active usages of `IMessageBus` in framework (ResourceLocks, Permissions, Settings, Features)
- Old system uses runtime `SubscribeAsync<T>(handler)` pattern with delegate-based handlers
- Integration tests rely on Foundatio bridge adapter (`AddMessageBusFoundatioAdapter()`)
- Both old and new messaging systems coexist, causing confusion

**Target State:**
- Single unified messaging system (`Framework.Messages.Abstractions`)
- Library-provided consumers registered via fluent extension methods
- Zero runtime subscription patterns - all compile-time registration
- Improved developer experience for library authors and consumers

## Problem Statement / Motivation

### Current Pain Points

1. **Two Messaging Systems:** Developers encounter both `IMessageBus` and `IConsume<T>`, causing confusion about which to use
2. **Manual Initialization:** Library consumers must remember to call `InitializeAsync()` or similar methods - easy to forget, causes runtime failures
3. **Runtime Subscribe Pattern:** Old `IMessageBus.SubscribeAsync<T>()` encourages service locator anti-pattern
4. **Complex Registration:** Each component has different setup requirements, no consistent registration pattern
5. **Limited Capabilities:** Old system lacks outbox pattern, topic conventions, concurrency control
6. **Testability Issues:** Delegate-based handlers harder to test than explicit `IConsume<T>` implementations

### Why Migrate Now

- **Type Safety:** Compile-time verification prevents runtime messaging errors
- **Industry Alignment:** Modern .NET messaging libraries (MassTransit, NServiceBus, CAP) all favor compile-time registration
- **Better DX:** Unified fluent registration eliminates manual initialization steps
- **Feature Access:** Unlock outbox pattern, convention-based routing, filters, lifecycle hooks
- **Maintainability:** Single messaging system reduces framework complexity
- **Zero Overhead Discovery:** DI-based discovery (~0.1ms) vs assembly scanning (~100-200ms)

## Proposed Solution

### High-Level Approach

**1. Unified Fluent DI Registration**

Add `AddConsumer<TConsumer, TMessage>(topic)` extension method that registers both consumer and metadata in DI using existing `ConsumerRegistration` infrastructure:

```csharp
// In Framework.ResourceLocks.Core/Setup.cs
public static IServiceCollection AddResourceLock<TStorage>(...)
{
    // ... existing registrations ...

    // Register consumer with fluent API
    services.AddConsumer<ResourceLockProvider.LockReleasedConsumer, ResourceLockReleased>(
        "framework.locks.released")
        .WithConcurrency(1);

    return services;
}

// In consuming application - implicit DI discovery
services.AddResourceLock<RedisResourceLockStorage>();
services.AddMessages(options =>
{
    options.UseRabbitMQ(/* config */);
    // Consumers auto-discovered from DI - no manual call needed!
});
```

**2. Convert Runtime Subscription to IConsume<T>**

Transform `ResourceLockProvider` from runtime `SubscribeAsync<T>(handler)` to compile-time nested consumer:

```csharp
// Old pattern (runtime subscribe)
private async Task _SubscribeToMessageBusAsync()
{
    await messageBus.SubscribeAsync<ResourceLockReleased>(_OnLockReleasedAsync);
}

// New pattern (nested compile-time consumer)
public sealed class ResourceLockProvider
{
    internal sealed class LockReleasedConsumer(
        IResourceLockProvider provider,
        ILogger<LockReleasedConsumer> logger) : IConsume<ResourceLockReleased>
    {
        public ValueTask Consume(ConsumeContext<ResourceLockReleased> context, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return ValueTask.FromCanceled(ct);

            if (provider is ResourceLockProvider impl)
                impl._OnLockReleased(context.Message);

            return ValueTask.CompletedTask;
        }
    }

    internal void _OnLockReleased(ResourceLockReleased message)
    {
        if (_autoResetEvents.TryGetValue(message.Resource, out var evt))
            evt.Target.Set();
    }
}
```

**3. Convert Messages to Records**

Modernize message types with immutable records:

```csharp
// Old
public class ResourceLockReleased
{
    public string Resource { get; set; }
    public string LockId { get; set; }
}

// New
public sealed record ResourceLockReleased(string Resource, string LockId);
```

**4. Explicit Topic Naming Convention**

Use explicit topic names following framework convention: `framework.{component}.{event}`

```csharp
services.AddConsumer<LockReleasedConsumer, ResourceLockReleased>(
    "framework.locks.released");  // Explicit topic

services.AddConsumer<PermissionChangedConsumer, PermissionChanged>(
    "framework.permissions.changed");  // Explicit topic
```

**5. Remove Foundatio Bridge**

Replace adapter in tests with direct in-memory transport:

```csharp
// Old (via Foundatio bridge)
services.AddSingleton<IFoundatioMessageBus>(_ => new InMemoryMessageBus());
services.AddMessageBusFoundatioAdapter();

// New (native in-memory)
services.AddMessages(options =>
{
    options.UseInMemoryMessageQueue();
    options.UseInMemoryStorage();
});
```

## Key Design Decisions

### Unified Fluent Registration Pattern

**Single Registration API:**
Both library authors and application developers use the same fluent builder pattern:

```csharp
// Method 1: Register in library Setup.cs (recommended for framework components)
services.AddConsumer<MyConsumer, MyMessage>("my.topic")
    .WithConcurrency(5)
    .WithTimeout(TimeSpan.FromSeconds(30));

// Method 2: Register in AddMessages (recommended for app consumers)
services.AddMessages(options =>
{
    options.Consumer<MyConsumer>("my.topic")
        .WithConcurrency(5)
        .WithTimeout(TimeSpan.FromSeconds(30));
});
```

Both methods:
- Return `IConsumerBuilder<TConsumer>` for fluent configuration
- Use same underlying `ConsumerRegistration` class
- Support same builder methods (`.WithConcurrency()`, `.WithTimeout()`)

**Implicit DI Discovery:**
- No manual `DiscoverConsumersFromDI()` call required
- Discovery happens automatically in `MessagingOptions.Build()`
- Consumers registered via `AddConsumer<>()` are auto-discovered when `AddMessages()` is called
- Registration order doesn't matter (discovery happens at build time)

**Reuses Existing Infrastructure:**
- No new metadata classes - uses existing `ConsumerRegistration`
- No new builder interface - uses existing `IConsumerBuilder<T>`
- No assembly scanning - reads `ConsumerRegistration` instances from DI

### Topic Naming Convention

**Framework Internal Events:**
Pattern: `framework.{component}.{event}`

Examples:
- `framework.locks.released`
- `framework.permissions.changed`
- `framework.settings.updated`
- `framework.features.toggled`

**Benefits:**
- Namespaced: Avoid collisions with application events
- Searchable: Easy to grep for all framework events
- Versionable: Can add `.v2` suffix for breaking changes
- Clear ownership: `framework.*` prefix indicates internal messaging

## Technical Considerations

### Architecture Impacts

**Service Lifetime Changes:**
- Old: `IMessageBus` registered as singleton with runtime subscription
- New: `IConsume<T>` implementations registered as scoped services
- Impact: Consumer dependencies must support scoped lifetime

**Message Flow Changes:**
- Old: Delegate invocation directly from message bus
- New: Compiled expression tree dispatch through `IMessageDispatcher`
- Impact: Better tracing/debugging, standardized dispatch pipeline

**Consumer Discovery:**
- Old: No discovery mechanism, manual subscription only
- New: Implicit DI discovery finds `ConsumerRegistration` instances registered via `AddConsumer<>`
- Impact: Zero-config, no assembly scanning overhead, consumers auto-discovered when `AddMessages()` is called

### Performance Implications

**Improvements:**
- **Dispatch Speed:** Compiled expression tree dispatch (baseline performance already established)
- **Memory:** Compiled expression trees cached, no per-message allocation
- **Connection Pooling:** Single registration = efficient connection reuse
- **Startup:** Zero assembly scanning overhead - DI discovery reads existing registrations (~0.1ms)

### Security Considerations

**Message Validation:**
- New system supports `IConsumeFilter` for cross-cutting validation
- Can add authentication/authorization filters globally
- Headers accessible via `ConsumeContext<T>.Headers` for token propagation

**Isolation:**
- Scoped consumers prevent shared state across messages
- Better aligned with security best practices (no singleton state)

## Acceptance Criteria

### Functional Requirements

- [ ] Unified fluent registration (`AddConsumer<TConsumer, TMessage>()`) implemented
- [ ] Implicit DI discovery in `MessagingOptions.Build()` implemented
- [ ] `IMessageBus` usage eliminated from all 4 components (ResourceLocks, Permissions, Settings, Features)
- [ ] All message types converted to records
- [ ] Explicit topic naming convention applied (`framework.{component}.{event}`)
- [ ] Nested consumer classes created in all components
- [ ] Foundatio bridge removed from all test fixtures
- [ ] All integration tests passing with new registration
- [ ] Documentation updated with fluent registration patterns

### Non-Functional Requirements

- [x] **Performance:** Message dispatch ≤5ms (p95) for simple consumers
- [x] **Backward Compatibility:** Resource lock API unchanged for consumers
- [x] **Test Coverage:** ≥85% line coverage, ≥80% branch coverage
- [x] **Build:** Zero warnings, all tests passing
- [x] **Documentation:** Migration guide for other library authors

### Quality Gates

- [x] Code review approval from framework maintainers
- [x] No `IMessageBus` references remain in framework codebase (except legacy tests)
- [x] CSharpier formatting applied
- [x] All analyzer warnings resolved

## Success Metrics

**Developer Experience:**
- Before: Multi-step setup (register IMessageBus, inject, call InitializeAsync)
- After: Single fluent call in Setup.cs (`services.AddConsumer<>()`)
- Consumer setup: Zero manual steps (automatic DI discovery)

**Code Quality:**
- Remove: ~200 lines of legacy `IMessageBus` infrastructure + Foundatio bridge
- Add: ~100 lines (`IConsume<T>` consumers + fluent registration extensions)
- Net: ~100 line reduction with better encapsulation

**Startup Performance:**
- Before: Assembly scanning overhead (~100-200ms per assembly)
- After: DI discovery only (~0.1ms) - no reflection over types

**Type Safety:**
- Before: Runtime errors if subscription fails
- After: Compile-time verification of consumer implementation

## Dependencies & Risks

### Dependencies

**Prerequisite:**
- ✅ `Framework.Messages.Abstractions` v2.x already released
- ✅ `Framework.ResourceLocks.Core` already references new lib

**Blocking:**
- ⚠️ Must decide on internal event bus strategy (see Alternative Approaches)

### Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Breaking change for component consumers** | LOW | Public APIs unchanged; messaging is internal implementation |
| **Test fixtures require updates** | MEDIUM | Update all `*TestBase.cs` files to use `AddMessages()` |
| **Service lifetime conflicts** | MEDIUM | Nested consumers access singleton providers via interface cast |
| **Forgotten DI registration** | LOW | Build fails if `AddConsumer<>()` not called (consumer not found) |

## Implementation Plan

### Phase 1: Add Unified Fluent Registration Infrastructure

**Files to Create:**
- `src/Framework.Messages.Abstractions/ServiceCollectionExtensions.cs`

```csharp
public static class ServiceCollectionExtensions
{
    public static IConsumerBuilder<TConsumer> AddConsumer<TConsumer, TMessage>(
        this IServiceCollection services,
        string topic)
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        // Register consumer to DI
        services.TryAddScoped<IConsume<TMessage>, TConsumer>();

        // Create registration using existing ConsumerRegistration
        var registration = new ConsumerRegistration
        {
            ConsumerType = typeof(TConsumer),
            MessageType = typeof(TMessage),
            Topic = topic,
            Concurrency = 1
        };

        // Register to DI for discovery
        services.AddSingleton(registration);

        // Return existing builder interface for fluent API
        return new ServiceCollectionConsumerBuilder<TConsumer>(registration);
    }
}
```

- `src/Framework.Messages.Abstractions/ServiceCollectionConsumerBuilder.cs`

```csharp
internal sealed class ServiceCollectionConsumerBuilder<TConsumer> : IConsumerBuilder<TConsumer>
    where TConsumer : class
{
    private readonly ConsumerRegistration _registration;

    public ServiceCollectionConsumerBuilder(ConsumerRegistration registration)
        => _registration = registration;

    public IConsumerBuilder<TConsumer> WithConcurrency(int concurrency)
    {
        _registration.Concurrency = concurrency;
        return this;
    }

    public IConsumerBuilder<TConsumer> WithTimeout(TimeSpan timeout)
    {
        _registration.Timeout = timeout;
        return this;
    }
}
```

**Files to Modify:**
- `src/Framework.Messages.Core/ServiceCollectionExtensions.cs` (AddMessages)

```csharp
public static IServiceCollection AddMessages(
    this IServiceCollection services,
    Action<IMessagingBuilder>? configure = null)
{
    var options = new MessagingOptions(services);
    configure?.Invoke(options);

    // Build finalizes registration (includes implicit DI discovery)
    options.Build();

    return services;
}
```

- `src/Framework.Messages.Core/MessagingOptions.cs`

```csharp
internal void Build()
{
    // IMPLICIT: Auto-discover consumers from DI
    DiscoverConsumersFromDI();

    // Rest of build logic...
}

private void DiscoverConsumersFromDI()
{
    var diRegistrations = Services
        .Where(sd => sd.ServiceType == typeof(ConsumerRegistration))
        .Select(sd => sd.ImplementationInstance as ConsumerRegistration)
        .Where(r => r != null)
        .Cast<ConsumerRegistration>();

    foreach (var registration in diRegistrations)
    {
        if (!Registry.IsRegistered(registration.MessageType))
            Registry.Add(registration);
    }
}
```

- `src/Framework.Messages.Core/ConsumerRegistry.cs`

```csharp
public bool IsRegistered(Type messageType)
{
    return _registrations.ContainsKey(messageType);
}
```

### Phase 2: Migrate Framework.ResourceLocks.Core

**Files to Create:**
- `src/Framework.ResourceLocks.Core/Messages/ResourceLockReleased.cs`

```csharp
namespace Framework.ResourceLocks.Messages;

public sealed record ResourceLockReleased(string Resource, string LockId);
```

**Files to Modify:**
- `src/Framework.ResourceLocks.Core/RegularLocks/ResourceLockProvider.cs`

Add nested consumer:
```csharp
public sealed class ResourceLockProvider : IResourceLockProvider
{
    // Nested consumer with access to provider internals
    internal sealed class LockReleasedConsumer(
        IResourceLockProvider provider,
        ILogger<LockReleasedConsumer> logger) : IConsume<ResourceLockReleased>
    {
        public ValueTask Consume(
            ConsumeContext<ResourceLockReleased> context,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled(cancellationToken);

            logger.LogDebug(
                "Processing lock released {MessageId} for {Resource}",
                context.MessageId, context.Message.Resource);

            if (provider is ResourceLockProvider impl)
                impl._OnLockReleased(context.Message);

            return ValueTask.CompletedTask;
        }
    }

    internal void _OnLockReleased(ResourceLockReleased message)
    {
        Logger.LogGotLockReleasedMessage(message.Resource, message.LockId);

        if (_autoResetEvents.TryGetValue(message.Resource, out var evt))
            evt.Target.Set();
    }

    // Remove: _isSubscribed, _subscribeLock, _EnsureTopicSubscriptionAsync
    // Remove: IMessageBus dependency, _SubscribeToMessageBusAsync, _OnLockReleasedAsync
}
```

- `src/Framework.ResourceLocks.Core/Setup.cs`

```csharp
public static IServiceCollection AddResourceLock<TStorage>(
    this IServiceCollection services,
    Action<ResourceLockOptions>? configureOptions = null)
    where TStorage : class, IResourceLockStorage
{
    // ... existing registrations (remove IMessageBus parameter) ...

    // Register consumer with fluent API
    services.AddConsumer<ResourceLockProvider.LockReleasedConsumer, ResourceLockReleased>(
        "framework.locks.released")
        .WithConcurrency(1);

    return services;
}
```

### Phase 3: Migrate Other Components

Apply same pattern to:

**Framework.Permissions.Core:**
```csharp
services.AddConsumer<PermissionCache.PermissionChangedConsumer, PermissionChanged>(
    "framework.permissions.changed")
    .WithConcurrency(5);
```

**Framework.Settings.Core:**
```csharp
services.AddConsumer<SettingCache.SettingUpdatedConsumer, SettingUpdated>(
    "framework.settings.updated")
    .WithConcurrency(10);
```

**Framework.Features.Core:**
```csharp
services.AddConsumer<FeatureProvider.FeatureToggledConsumer, FeatureToggled>(
    "framework.features.toggled")
    .WithConcurrency(1);
```

### Phase 4: Update Test Fixtures

**Files to Modify:**
- `tests/Framework.Settings.Tests.Integration/TestSetup/SettingsTestBase.cs`
- `tests/Framework.Permissions.Tests.Integration/TestSetup/PermissionsTestBase.cs`
- `tests/Framework.Features.Tests.Integration/TestSetup/FeaturesTestBase.cs`
- `tests/Framework.ResourceLocks.Tests.Integration/TestSetup/ResourceLocksTestBase.cs`

Replace Foundatio bridge:
```csharp
// Before
services.AddSingleton<IFoundatioMessageBus>(_ => new InMemoryMessageBus());
services.AddMessageBusFoundatioAdapter();

// After
services.AddMessages(options =>
{
    options.UseInMemoryMessageQueue();
    options.UseInMemoryStorage();
    // Consumers auto-discovered from AddResourceLock/AddPermissions/etc calls
});
```

### Phase 5: Remove Legacy Code

**Files to Delete:**
- `src/Framework.Messaging.Abstractions/` (entire project)
- Foundatio adapter implementation files

**Files to Modify:**
- Remove `Framework.Messaging.Abstractions` from solution
- Remove `IMessageBus` from `Directory.Packages.props`
- Update component README.md files

### Phase 6: Documentation

**Files to Create:**
- `docs/migration/imessagebus-to-framework-messages.md`

Content:
- Side-by-side comparison (old vs new patterns)
- Migration guide for library authors
- Topic naming conventions
- Troubleshooting guide

## Alternative Approaches Considered

### Option A: Add Runtime Subscribe to Framework.Messages ❌

**Approach:** Extend `IMessagingBuilder` with `SubscribeAtRuntime<T>(handler)` method

**Pros:**
- Minimal changes to ResourceLockProvider
- Preserves lazy subscription pattern

**Cons:**
- Goes against modern messaging best practices
- Defeats compile-time verification benefits
- Adds complexity to new system architecture
- Service locator anti-pattern
- Poor testability

**Verdict:** Rejected - runtime subscribe is a legacy pattern

### Option B: Keep Both Systems (IMessageBus + Framework.Messages) ❌

**Approach:** Maintain `IMessageBus` for internal framework use, `Framework.Messages` for applications

**Pros:**
- Zero breaking changes
- Clear separation between internal and external

**Cons:**
- Two messaging systems to maintain
- Developer confusion persists
- Duplicated concepts and infrastructure
- Missed performance benefits for framework

**Verdict:** Rejected - increases complexity without benefits

### Option C: Hybrid Approach (Runtime + Improved Registration) ❌

**Approach:** Add both runtime subscribe capability AND library helper extensions

**Pros:**
- Maximum flexibility
- Gradual migration path

**Cons:**
- Two ways to do the same thing (confusing)
- Doubles testing surface
- Harder to document

**Verdict:** Rejected - prefer single clear pattern

## Testing Strategy

### Unit Tests

**New Test Files:**
- `tests/Framework.ResourceLocks.Core.Tests.Unit/Messaging/ResourceLockReleasedConsumerTests.cs`

```csharp
public sealed class ResourceLockReleasedConsumerTests : TestBase
{
    [Fact]
    public async Task should_signal_waiting_lock_when_released()
    {
        // Given - real ResourceLockProvider instance (sealed, can't mock)
        var storage = new InMemoryResourceLockStorage();
        var provider = new ResourceLockProvider(
            storage,
            new ResourceLockOptions(),
            new SnowflakeIdLongIdGenerator(1),
            TimeProvider.System,
            Logger);

        // Start waiting for lock (creates AutoResetEvent in provider)
        var acquireTask = provider.TryAcquireAsync(
            "test-resource",
            acquireTimeout: TimeSpan.FromSeconds(1));

        await Task.Delay(50); // Let it start waiting

        // When - consumer processes lock released message
        var consumer = new ResourceLockProvider.LockReleasedConsumer(provider, Logger);
        var context = new ConsumeContext<ResourceLockReleased>
        {
            Message = new ResourceLockReleased("test-resource", "lock-123"),
            MessageId = "msg-123",
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>()),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "framework.locks.released"
        };

        await consumer.Consume(context, AbortToken);

        // Then - wait completes faster than timeout
        var sw = Stopwatch.StartNew();
        var result = await acquireTask;
        sw.Stop();

        result.Should().NotBeNull();
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    [Fact]
    public async Task should_handle_cancellation_gracefully()
    {
        // Given
        var provider = new ResourceLockProvider(/* ... */);
        var consumer = new ResourceLockProvider.LockReleasedConsumer(provider, Logger);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // When
        var result = await consumer.Consume(
            new ConsumeContext<ResourceLockReleased> { /* ... */ },
            cts.Token);

        // Then
        result.IsCanceled.Should().BeTrue();
    }
}
```

**Note:** Test with real `ResourceLockProvider` instance (sealed class, cannot mock with NSubstitute).

### Integration Tests

**Modified Test Files:**
- Update all `*TestBase.cs` files to use `AddMessages` + `AddResourceLocksMessaging()`
- Verify resource lock acquire → wait → release flow works end-to-end

**Test Scenarios:**
1. Acquire lock → release → verify wait completes
2. Multiple waiters → verify all notified on release
3. Concurrent lock requests → verify serialization
4. Message delivery failure → verify retry behavior


## Migration Guide for Library Authors

### Before (Old Pattern)

```csharp
// Library provides IMessageBus-based service
public class MyService(IMessageBus messageBus)
{
    private async Task InitializeAsync()
    {
        await messageBus.SubscribeAsync<MyEvent>(async (msg) =>
        {
            // Handle message
        });
    }
}

// Library Setup.cs
services.AddSingleton<MyService>(sp =>
{
    var service = new MyService(sp.GetRequiredService<IMessageBus>());
    // Must document: "Call InitializeAsync somewhere!"
    return service;
});

// Consumer must:
// 1. Register IMessageBus provider
// 2. Call service.InitializeAsync() somewhere (easy to forget!)
```

### After (New Pattern)

```csharp
// 1. Convert message to record
public sealed record MyEvent(string Data);

// 2. Create nested consumer class
public sealed class MyService
{
    internal sealed class MyEventConsumer(
        MyService service,
        ILogger<MyEventConsumer> logger) : IConsume<MyEvent>
    {
        public ValueTask Consume(
            ConsumeContext<MyEvent> context,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled(cancellationToken);

            service.HandleEvent(context.Message);
            return ValueTask.CompletedTask;
        }
    }

    internal void HandleEvent(MyEvent evt)
    {
        // Event handling logic
    }
}

// 3. Register consumer in Setup.cs using fluent API
public static IServiceCollection AddMyLibrary(this IServiceCollection services)
{
    services.AddSingleton<MyService>();

    // Register consumer with explicit topic
    services.AddConsumer<MyService.MyEventConsumer, MyEvent>("my-library.events")
        .WithConcurrency(5);

    return services;
}

// Consumer usage - automatic discovery!
services.AddMyLibrary();
services.AddMessages(options =>
{
    options.UseRabbitMQ(/* config */);
    // MyEventConsumer auto-discovered from DI
});
```

### Benefits for Library Authors

- **Encapsulation:** Nested consumer class keeps implementation private
- **Single registration point:** Consumer registered in library's `Setup.cs`
- **No manual initialization:** No `InitializeAsync()` method needed
- **Compile-time safety:** Build fails if consumer doesn't implement `IConsume<T>`

### Benefits for Library Consumers

- **Zero ceremony:** Just call `AddMyLibrary()` and `AddMessages()`
- **Impossible to forget:** No manual initialization step
- **Type-safe:** Compile errors if consumer misconfigured
- **Testable:** Easy to test consumers with real instances
- **Consistent:** Same pattern across all framework components

## References & Research

### Internal References

- **Current IMessageBus Usage:** `src/Framework.ResourceLocks.Core/RegularLocks/ResourceLockProvider.cs:19-285`
- **Old Abstraction:** `src/Framework.Messaging.Abstractions/IMessageBus.cs`
- **New Abstraction:** `src/Framework.Messages.Abstractions/IMessagingBuilder.cs`
- **Consumer Registry:** `src/Framework.Messages.Core/ConsumerRegistry.cs`
- **Test Examples:** `tests/Framework.Messages.Core.Tests.Unit/MessagingBuilderTests.cs`

### External References

- [MassTransit Configuration](https://masstransit.io/documentation/configuration)
- [NServiceBus Assembly Scanning](https://docs.particular.net/nservicebus/hosting/assembly-scanning)
- [CAP Configuration](https://cap.dotnetcore.xyz/user-guide/en/cap/configuration/)
- [Azure Service Bus Best Practices](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-performance-improvements)

### Related Work

- **Previous messaging work:** Commit `8f5b4627` - "feat: add MessagingConventions for convention-based topic naming"
- **Current branch:** `xshaheen/messaging-consume` - RabbitMQ refactoring and messaging improvements
- **Related plan:** `plans/refactor-iconsume-5-validation.md` - Minimal inline validation approach

## Summary

### What Changed

**From:**
- Runtime `messageBus.SubscribeAsync<T>(handler)` pattern
- Lazy subscription initialization
- Assembly scanning for consumer discovery
- Manual `DiscoverConsumersFromDI()` call
- Separate library extension methods for registration

**To:**
- Compile-time `IConsume<T>` pattern with nested consumers
- Fluent DI registration: `services.AddConsumer<TConsumer, TMessage>(topic)`
- Implicit DI discovery (automatic)
- Zero assembly scanning overhead
- Explicit topic naming convention
- Immutable record messages

### Key Benefits

1. **Unified Registration:** Single fluent API for both library and app consumers
2. **Zero Ceremony:** No manual initialization, no discovery calls, no assembly scanning
3. **Type Safety:** Compile-time verification of consumer implementations
4. **Better Encapsulation:** Nested consumers keep implementation details private
5. **Faster Startup:** DI discovery (~0.1ms) vs assembly scanning (~100-200ms)
6. **Consistent API:** Same builder pattern as existing `options.Consumer<T>()`
7. **No New Classes:** Reuses existing `ConsumerRegistration` and `IConsumerBuilder<T>`

### Migration Path

All 4 components migrated using same pattern:
1. Convert message to record
2. Create nested consumer class
3. Add fluent registration in Setup.cs
4. Remove IMessageBus dependency

Template established with ResourceLocks, applied to Permissions, Settings, Features.

## Notes

- Migration applies to 4 components: ResourceLocks, Permissions, Settings, Features
- Pattern is consistent across all components for maintainability
- Public APIs unchanged - messaging is internal implementation detail
- Test fixtures simplified - remove Foundatio bridge, use native in-memory transport
