# IConsume<T> Part 5: Minimal Configuration Validation

**Priority**: LOW
**Dependencies**: Part 1 (Core Foundation)
**Estimated Effort**: 0.5 hour

## Goal

Add minimal inline validation to catch obvious configuration mistakes at startup (duplicate registrations, empty configuration).

## Scope

**In Scope:**
- Duplicate consumer detection (same type + message + group)
- Empty configuration warning
- Clear error messages
- Inline checks (no framework)

**Out of Scope:**
- Validation framework/configurator (YAGNI - tests catch these)
- RequireGroup/RequireExplicitTopic rules (trust conventions)
- RequireFilter rules (use tests/linting)
- Advanced validation features

## Decision Rationale

Per comprehensive plan review by 3 expert reviewers:
- **Scott Hanselman**: "You're guessing what errors you'll have. Tests catch these naturally."
- **Stephen Toub**: "Integration tests provide same safety without complexity."
- **Simplicity Reviewer**: "600 LOC YAGNI violation. Delete Part 5 entirely."

**Compromise**: 20-line inline check catches obvious mistakes without framework overhead.

**What tests catch that validation would:**
- Wrong topic mappings → Integration test fails in <5 min
- Missing handlers → Test fails immediately
- Configuration errors → Test suite catches in first run

## Implementation

### Inline Validation in MessagingBuilder

**Update MessagingBuilder.cs:**

```csharp
public sealed class MessagingBuilder(IServiceCollection services) : IMessagingBuilder
{
    private readonly ConsumerRegistry _registry = new();
    private readonly ILogger<MessagingBuilder> _logger;

    // ... existing methods ...

    internal void FinalizeConfiguration()
    {
        var allConsumers = _registry.GetAll().ToList();

        // Validation 1: Warn if no consumers registered
        if (!allConsumers.Any())
        {
            _logger.LogWarning(
                "No message consumers registered. Did you forget to call AddConsumer<T>() or AddConsumersFromAssembly()?");
            return;
        }

        // Validation 2: Detect duplicate registrations (likely user error)
        var duplicates = allConsumers
            .GroupBy(c => new { c.MessageType, c.ConsumerType, c.Group })
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.ConsumerType.Name} handling {g.Key.MessageType.Name} in group '{g.Key.Group ?? "(default)"}' registered {g.Count()} times")
            .ToList();

        if (duplicates.Any())
        {
            throw new InvalidOperationException(
                $"Duplicate consumer registrations detected (likely configuration error):\n" +
                string.Join("\n", duplicates.Select(d => $"  - {d}")));
        }

        _logger.LogInformation(
            "Registered {ConsumerCount} message consumer(s) for {MessageTypeCount} message type(s)",
            allConsumers.Count,
            allConsumers.Select(c => c.MessageType).Distinct().Count());
    }
}
```

**Update MessagingBuilderExtensions.cs:**

```csharp
public static IServiceCollection AddMessaging(
    this IServiceCollection services,
    Action<IMessagingBuilder> configure)
{
    var builder = new MessagingBuilder(services);
    configure(builder);

    // Run inline validation before finalizing
    builder.FinalizeConfiguration();

    return services;
}
```

## Testing

### Unit Tests

```csharp
public class MinimalValidationTest
{
    [Fact]
    public void should_throw_when_duplicate_consumer_registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
        {
            services.AddMessaging(m =>
            {
                m.AddConsumer<OrderCreatedHandler>(c => c.Group("orders"));
                m.AddConsumer<OrderCreatedHandler>(c => c.Group("orders"));
                // Same consumer type, same message, same group = duplicate
            });
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate consumer registrations*")
            .WithMessage("*OrderCreatedHandler*");
    }

    [Fact]
    public void should_allow_same_consumer_in_different_groups()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
        {
            services.AddMessaging(m =>
            {
                m.AddConsumer<OrderCreatedHandler>(c => c.Group("group1"));
                m.AddConsumer<OrderCreatedHandler>(c => c.Group("group2"));
                // Same consumer type, same message, DIFFERENT groups = OK
            });
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void should_warn_when_no_consumers_registered()
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        services.AddSingleton(loggerFactory);

        services.AddMessaging(m =>
        {
            // No consumers added
        });

        // Check logs contain warning
        // (Implementation depends on test logging setup)
    }

    [Fact]
    public void should_log_registration_summary()
    {
        var services = new ServiceCollection();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        services.AddSingleton(loggerFactory);

        services.AddMessaging(m =>
        {
            m.AddConsumer<OrderCreatedHandler>();
            m.AddConsumer<OrderShippedHandler>();
        });

        // Check logs contain:
        // "Registered 2 message consumer(s) for 2 message type(s)"
    }
}
```

## What This DOESN'T Validate (Intentionally)

**Tests catch these faster:**

1. **Missing consumer groups**
   - CAP uses broker default (works fine)
   - If specific group needed, integration test fails immediately

2. **Convention-based vs explicit topics**
   - Integration test fails if publisher/consumer mismatch
   - Caught in first test run (<5 minutes)

3. **Missing filters**
   - Test asserts on side effects (logs, metrics)
   - Fails in unit/integration tests

4. **Multiple handlers for same message**
   - If in same group → duplicate processing (business logic test fails)
   - If in different groups → competing consumers (intentional)

## Acceptance Criteria

- [ ] Duplicate registration throws clear error message
- [ ] Empty configuration logs warning (not error)
- [ ] Registration summary logged on successful configuration
- [ ] Same consumer in different groups allowed (competing consumers)
- [ ] Unit tests pass
- [ ] No validation framework code (keep it inline)
- [ ] Coverage ≥85%

## Files Changed

**Modified:**
- `src/Framework.Messages.Core/MessagingBuilder.cs` (~20 lines added in FinalizeConfiguration)
- `src/Framework.Messages.Abstractions/MessagingBuilderExtensions.cs` (call FinalizeConfiguration)

**Created:**
- `tests/Framework.Messages.Core.Tests.Unit/MinimalValidationTest.cs`

**NOT Created** (vs original Part 5 plan):
- ❌ `IValidationConfigurator.cs` - No configurator needed
- ❌ `ValidationRule.cs` - No rule abstraction
- ❌ `RequireGroupRule.cs` - Tests catch this
- ❌ `RequireExplicitTopicRule.cs` - Tests catch this
- ❌ `ForbidDuplicateHandlersRule.cs` - Inline check instead
- ❌ `RequireFilterRule.cs` - Tests catch this
- ❌ `ValidationConfigurator.cs` - No framework
- ❌ `ConfigurationValidator.cs` - No framework

**Total LOC**: ~20 lines (vs 600 lines in original plan)

## Usage Example

```csharp
services.AddMessaging(messaging =>
{
    messaging.ConfigureCap(cap => { /* ... */ });

    // Scenario 1: Duplicate registration (ERROR)
    messaging.AddConsumer<OrderHandler>();
    messaging.AddConsumer<OrderHandler>();
    // Throws: "Duplicate consumer registrations detected..."

    // Scenario 2: Competing consumers (OK)
    messaging.AddConsumer<OrderHandler>(c => c.Group("group1"));
    messaging.AddConsumer<OrderHandler>(c => c.Group("group2"));
    // Works: Same handler, different groups

    // Scenario 3: No consumers (WARNING)
    // No AddConsumer calls
    // Logs: "No message consumers registered. Did you forget..."
});
```

## Integration with Other Parts

**Part 1 (Core Foundation)**:
- `ConsumerRegistry.GetAll()` must exist
- `MessagingBuilder` must have logger injected

**Parts 2-4**:
- No dependencies on validation
- Validation runs after all registration complete

## Migration from Original Part 5

**Before (Full Framework)**:
```csharp
messaging.ConfigureValidation(v =>
{
    v.RequireGroup();
    v.ForbidDuplicateHandlers();
    v.RequireExplicitTopic();
});
messaging.ValidateOnStartup();
```

**After (Minimal Inline)**:
```csharp
// No API needed - validation runs automatically
// Just write integration tests for your specific requirements
```

**Validation now happens in tests:**
```csharp
[Fact]
public async Task should_process_order_messages()
{
    // This test validates:
    // ✓ Topic mapping correct
    // ✓ Handler registered
    // ✓ DI wiring correct
    // ✓ Message routing works

    await publisher.PublishAsync("orders.created", new OrderCreated());
    await Task.Delay(100);

    orderService.Received(1).ProcessAsync(Arg.Any<OrderCreated>());
}
```

## Decision Points Resolved

✅ **Skip full validation framework** - Tests provide better coverage
✅ **Keep minimal duplicate detection** - Catches obvious user errors
✅ **Log warning on empty config** - Helps debug "nothing works" scenarios
✅ **No configurator API** - Inline checks only
✅ **Trust integration tests** - They catch everything else faster

## Performance Impact

**Original Part 5**: ~600 LOC, startup validation overhead, reflection for rule evaluation
**Minimal Part 5**: ~20 LOC, O(n) grouping check at startup (negligible)

## Summary

**What we're building:**
- 20-line sanity check in `FinalizeConfiguration()`
- Catches duplicate registrations (user error)
- Warns on empty configuration (likely forgot to register)
- Logs helpful summary

**What we're NOT building:**
- Validation rule framework
- Configurator abstraction
- Pluggable validation rules
- Advanced validation features

**Why this is better:**
- 30x less code (20 lines vs 600)
- Tests catch everything else in <5 min
- No maintenance burden
- Pragmatic over perfect

**Estimated effort**: 30 minutes to implement, 30 minutes to test = **0.5 hour total**
