# IConsume<T> Surgical Migration Plan

**Status**: Draft - Adapting to existing infrastructure
**Approach**: Surgical changes to existing codebase, not full rewrite

## Current State Analysis

### What Already Exists ✅
1. **Abstractions (Framework.Messages.Abstractions)**
   - `IConsume<T>` interface - fully documented
   - `ConsumeContext<T>` class - complete with all metadata
   - `IMessagingBuilder` interface - has `ScanConsumers()`, `Consumer<T>()`, `WithTopicMapping<T>()`
   - `IConsumerBuilder<T>` interface - has `Topic()`, `Group()`, `WithConcurrency()`
   - `IConsumeFilter` interface - for cross-cutting concerns
   - `IMessageDispatcher` + `CompiledMessageDispatcher` - high-performance dispatcher (NOT USED YET)

2. **Old Pattern (TO BE REMOVED)**
   - `IConsumer` marker interface - empty marker for discovery
   - `[CapSubscribe]` attribute - topic/group configuration
   - `[FromCap]` attribute - parameter binding
   - Reflection-based parameter binding in `SubscribeInvoker`

3. **Infrastructure (Framework.Messages.Core)**
   - `CapBuilder` - fluent configuration API
   - `AddCap()` - main entry point in `Setup.cs`
   - `ConsumerServiceSelector` - discovers `IConsumer` implementations
   - `SubscribeInvoker` - invokes handlers via reflection
   - CAP integration - transport, storage, retry, DLQ

### What's Missing ❌
1. No implementation of `IMessagingBuilder` interface
2. No implementation of `IConsumerBuilder<T>` interface
3. No consumer registry to store metadata
4. `CompiledMessageDispatcher` exists but isn't used
5. `ConsumerServiceSelector` only discovers `IConsumer`, not `IConsume<T>`
6. `SubscribeInvoker` uses reflection, not compiled dispatcher

## Surgical Migration Strategy

### Phase 1: Core Registry & Builder Implementation
**Goal**: Implement existing interfaces, create consumer registry

**Files to Create:**
1. `src/Framework.Messages.Core/ConsumerMetadata.cs`
   ```csharp
   public sealed record ConsumerMetadata(
       Type MessageType,
       Type ConsumerType,
       string Topic,
       string? Group,
       byte Concurrency
   );
   ```

2. `src/Framework.Messages.Core/ConsumerRegistry.cs`
   ```csharp
   public sealed class ConsumerRegistry
   {
       private readonly List<ConsumerMetadata> _consumers = [];

       public void Register(ConsumerMetadata metadata) => _consumers.Add(metadata);
       public IReadOnlyList<ConsumerMetadata> GetAll() => _consumers;
   }
   ```

3. `src/Framework.Messages.Core/MessagingBuilder.cs`
   ```csharp
   internal sealed class MessagingBuilder : IMessagingBuilder
   {
       private readonly IServiceCollection _services;
       private readonly ConsumerRegistry _registry;

       public IMessagingBuilder ScanConsumers(Assembly assembly) { }
       public IConsumerBuilder<T> Consumer<T>() { }
       public IMessagingBuilder WithTopicMapping<T>(string topic) { }
   }
   ```

4. `src/Framework.Messages.Core/ConsumerBuilder.cs`
   ```csharp
   internal sealed class ConsumerBuilder<TConsumer> : IConsumerBuilder<TConsumer>
   {
       public IConsumerBuilder<TConsumer> Topic(string topic, bool isPartial = false) { }
       public IConsumerBuilder<TConsumer> Group(string group) { }
       public IConsumerBuilder<TConsumer> WithConcurrency(byte maxConcurrent) { }
       public IMessagingBuilder Build() { }
   }
   ```

**Files to Modify:**
- `src/Framework.Messages.Core/Setup.cs` - Add `AddMessages()` extension method

**Estimated Time**: 2-3 hours

### Phase 2: Update Discovery (ConsumerServiceSelector)
**Goal**: Make selector discover `IConsume<T>` implementations instead of `IConsumer`

**Files to Modify:**
1. `src/Framework.Messages.Core/Internal/IConsumerServiceSelector.Default.cs`
   - Change `FindConsumersFromInterfaceTypes()` to:
     - Look for `IConsume<T>` instead of `IConsumer`
     - Get metadata from `ConsumerRegistry`
     - Build `ConsumerExecutorDescriptor` for each registered consumer
   - Remove attribute-based discovery code
   - Remove `[CapSubscribe]` and `[FromCap]` handling

**Key Changes:**
```csharp
// OLD: Look for IConsumer marker
var subscribeTypeInfo = typeof(IConsumer).GetTypeInfo();

// NEW: Get from registry
var registry = serviceProvider.GetRequiredService<ConsumerRegistry>();
foreach (var metadata in registry.GetAll())
{
    // Build descriptor from metadata
}
```

**Estimated Time**: 2-3 hours

### Phase 3: Update Invocation (SubscribeInvoker)
**Goal**: Use `CompiledMessageDispatcher` instead of reflection

**Files to Modify:**
1. `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs`
   - Remove parameter binding logic (no more `[FromCap]`)
   - Remove `ObjectMethodExecutor` reflection code
   - Use `CompiledMessageDispatcher.DispatchAsync<T>()`
   - Build `ConsumeContext<T>` from `MediumMessage`

**Key Changes:**
```csharp
// OLD: Reflection-based invocation
var executor = ObjectMethodExecutor.Create(methodInfo, ...);
await executor.ExecuteAsync(obj, parameters);

// NEW: Compiled dispatcher
var dispatcher = provider.GetRequiredService<IMessageDispatcher>();
var context = new ConsumeContext<T> { Message = message, ... };
await dispatcher.DispatchAsync(context, cancellationToken);
```

**Estimated Time**: 2-3 hours

### Phase 4: Delete Old Pattern
**Goal**: Remove `IConsumer`, attributes, and old tests

**Files to Delete:**
- `src/Framework.Messages.Abstractions/IConsumer.cs` ✅ (already deleted)
- `src/Framework.Messages.Abstractions/Attributes.cs` ✅ (already deleted)
- `tests/Framework.Messages.Core.Tests.Unit/ConsumerServiceSelectorTest.cs` ✅ (already deleted)

**Estimated Time**: 30 minutes (already done)

### Phase 5: Tests
**Goal**: Write comprehensive tests for new pattern

**Files to Create:**
1. `tests/Framework.Messages.Core.Tests.Unit/MessagingBuilderTests.cs`
   - Test `Consumer<T>()` registration
   - Test `ScanConsumers()` discovery
   - Test topic/group/concurrency configuration
   - Test multi-message handlers

2. `tests/Framework.Messages.Core.Tests.Unit/ConsumerRegistryTests.cs`
   - Test metadata storage and retrieval

3. `tests/Framework.Messages.Core.Tests.Integration/IConsumeIntegrationTests.cs`
   - End-to-end message flow
   - Verify compiled dispatcher is used
   - Test DI scope management

**Estimated Time**: 3-4 hours

## Total Effort Estimate
- Phase 1: 2-3 hours
- Phase 2: 2-3 hours
- Phase 3: 2-3 hours
- Phase 4: Done
- Phase 5: 3-4 hours
- **Total: 9-13 hours (1-2 days)**

## Implementation Order
1. ✅ Delete old pattern files (Phase 4 - DONE)
2. Create registry and builder implementations (Phase 1)
3. Update `ConsumerServiceSelector` (Phase 2)
4. Update `SubscribeInvoker` (Phase 3)
5. Write tests (Phase 5)

## Key Differences from Original Plan
| Original Plan | Surgical Plan |
|--------------|---------------|
| Create new `IMessagingBuilder` | **Implement existing** `IMessagingBuilder` |
| New fluent API design | **Use existing** fluent API |
| Build entire system from scratch | **Surgical changes** to existing system |
| 5-part plan (7-9 days) | **Single focused plan** (1-2 days) |
| Full feature set | **MVP only** (conventions in future) |

## What We're NOT Doing (Can Add Later)
- ❌ Conventions (kebab-case topics, prefixes) - Part 2 from original plan
- ❌ Retry policies (use CAP's existing retry) - Part 3 from original plan
- ❌ Filters (use CAP's existing filters) - Part 4 from original plan
- ❌ Validation (tests catch issues) - Part 5 from original plan

## Success Criteria
- [ ] `IConsume<T>` handlers discovered via `ScanConsumers()`
- [ ] Manual registration via `Consumer<T>()` works
- [ ] Topic/group/concurrency configuration works
- [ ] `CompiledMessageDispatcher` used (5-8x faster than reflection)
- [ ] End-to-end message flow works
- [ ] All tests pass
- [ ] No `IConsumer` or `[CapSubscribe]` in codebase

## Breaking Changes
**YES - This is a breaking change:**
- Old: `public class Handler : IConsumer { [CapSubscribe("topic")] Task Handle() }`
- New: `public class Handler : IConsume<Message> { ValueTask Consume(ConsumeContext<Message> ctx) }`

Users must migrate all handlers. Migration guide needed.
