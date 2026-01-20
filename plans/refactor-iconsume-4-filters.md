# IConsume<T> Part 4: Filters (CAP Wrapper)

**Priority**: MEDIUM-LOW
**Dependencies**: Part 1 (Core Foundation)
**Estimated Effort**: 0.3 day (2.5 hours)

## Goal

Expose CAP's built-in subscriber filter system through a cleaner API in the `AddMessaging()` builder.

## Scope

**In Scope:**
- Wrapper API for CAP's `SubscriberFilters`
- Global filter registration
- Simple, clean configuration interface
- Comprehensive XML documentation

**Out of Scope:**
- Custom filter pipeline implementation (CAP already has this)
- Per-consumer filters (CAP only supports global filters)
- Filter ordering control (CAP executes in registration order)
- Custom filter execution logic (trust CAP)

## Architecture Decision

**DON'T reimplement filter pipeline**. CAP already provides:
- `ISubscribeFilter` interface (which `IConsumeFilter` extends)
- Filter execution pipeline with pre/post/exception hooks
- Filter registration via `SubscriberFilters` collection

**DO provide cleaner API** over CAP's filter registration to match our builder pattern.

## Pre-Implementation Verification

**MUST verify these CAP behaviors before implementation:**

1. **Insertion order preservation**: Does `CapOptions.SubscriberFilters` preserve insertion order? (Check CAP source)
2. **Cancellation token support**: Does `ExecutingContext` expose `CancellationToken`? (Check CAP API)
3. **Exception handling**: What happens when filter throws during `OnSubscribeExecutingAsync`?
   - Does it route to DLQ?
   - Does it trigger `OnSubscribeExceptionAsync` on all filters?
   - Does it stop filter pipeline execution?
4. **Scoped lifetime**: Verify CAP creates new DI scope per message (confirms scoped registration is correct)

**Action**: Review CAP source code at https://github.com/dotnetcore/CAP before implementation.

## Implementation

### Phase 1: Filter Registration Wrapper

**Add to IMessagingBuilder.cs:**

```csharp
public interface IMessagingBuilder
{
    // Existing methods...

    /// <summary>
    /// Registers a global filter that executes for all consumed messages.
    /// Filters execute in registration order before and after message handlers.
    /// </summary>
    /// <typeparam name="TFilter">Filter type implementing <see cref="IConsumeFilter"/>.</typeparam>
    /// <returns>The messaging builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Filters are registered with scoped lifetime. CAP creates a new DI scope per message,
    /// so each message gets a fresh filter instance with its own dependency graph.
    /// </para>
    /// <para>
    /// <strong>Execution order:</strong>
    /// <list type="number">
    /// <item>Filter1.OnSubscribeExecutingAsync (pre-processing)</item>
    /// <item>Filter2.OnSubscribeExecutingAsync (pre-processing)</item>
    /// <item>Message handler</item>
    /// <item>Filter2.OnSubscribeExecutedAsync (post-processing, reverse order)</item>
    /// <item>Filter1.OnSubscribeExecutedAsync (post-processing, reverse order)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Exception handling:</strong> If an exception occurs during message processing
    /// (in handler or filter), CAP calls OnSubscribeExceptionAsync on all filters in reverse
    /// registration order. The exception is then handled according to retry/DLQ configuration.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddMessaging(m =>
    /// {
    ///     m.AddFilter&lt;LoggingFilter&gt;();
    ///     m.AddFilter&lt;MetricsFilter&gt;();
    ///     m.AddFilter&lt;ValidationFilter&gt;();
    /// });
    /// </code>
    /// </example>
    IMessagingBuilder AddFilter<TFilter>() where TFilter : class, IConsumeFilter;
}
```

**Note**: `IConsumeFilter` already exists in Framework.Messages.Abstractions and extends CAP's `ISubscribeFilter`.

### Phase 2: Implementation in MessagingBuilder

**Update MessagingBuilder.cs:**

```csharp
public sealed class MessagingBuilder(IServiceCollection services) : IMessagingBuilder
{
    private readonly List<Type> _filterTypes = [];

    /// <inheritdoc />
    public IMessagingBuilder AddFilter<TFilter>() where TFilter : class, IConsumeFilter
    {
        _filterTypes.Add(typeof(TFilter));

        // Register filter in DI with scoped lifetime
        // CAP creates DI scope per message, ensuring thread-safe isolated filter instances
        services.AddScoped<TFilter>();

        return this;
    }

    internal void ApplyToCapOptions(CapOptions capOptions)
    {
        // Apply retry/DLQ configuration (from Part 3)
        // ...

        // Apply filters to CAP's subscriber pipeline
        foreach (var filterType in _filterTypes)
        {
            try
            {
                capOptions.SubscriberFilters.Add(filterType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to register filter {filterType.Name} with CAP. " +
                    $"Ensure the type implements IConsumeFilter correctly.",
                    ex);
            }
        }
    }
}
```

**Design rationale:**
- **Deferred registration**: Filters stored in `_filterTypes` then applied in `ApplyToCapOptions()` because `CapOptions` isn't available until `AddMessaging()` configures CAP
- **Scoped lifetime**: CAP creates new DI scope per message, so scoped registration provides per-message filter instances
- **Error handling**: Wrap CAP registration to provide better error messages if registration fails

### Phase 3: Integration with CAP Configuration

**Already integrated in Part 3** via `ApplyToCapOptions()` in `AddMessaging()`.

No additional changes needed - filters are registered alongside retry/DLQ configuration.

## Testing

### Unit Tests

**FilterConfigurationTest.cs:**

```csharp
public class FilterConfigurationTest
{
    [Fact]
    public void should_register_filter_in_di_and_cap()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.AddFilter<TestFilter>();
        });

        var capOptions = services.BuildServiceProvider()
            .GetRequiredService<IOptions<CapOptions>>()
            .Value;

        // Verify DI registration
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(TestFilter) &&
            sd.Lifetime == ServiceLifetime.Scoped);

        // Verify CAP registration
        capOptions.SubscriberFilters.Should().Contain(typeof(TestFilter));
    }

    [Fact]
    public void should_register_multiple_filters()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.AddFilter<TestFilter>();
            m.AddFilter<AnotherTestFilter>();
        });

        var capOptions = services.BuildServiceProvider()
            .GetRequiredService<IOptions<CapOptions>>()
            .Value;

        capOptions.SubscriberFilters
            .Should().Contain(typeof(TestFilter))
            .And.Contain(typeof(AnotherTestFilter));
    }

    [Fact]
    public void should_handle_duplicate_filter_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.AddFilter<TestFilter>();
            m.AddFilter<TestFilter>(); // Duplicate
        });

        var capOptions = services.BuildServiceProvider()
            .GetRequiredService<IOptions<CapOptions>>()
            .Value;

        // Both registrations should be present (CAP's behavior)
        // Users responsible for avoiding duplicates
        capOptions.SubscriberFilters
            .Count(t => t == typeof(TestFilter))
            .Should().Be(2);
    }

    [Fact]
    public void should_fail_when_filter_has_missing_dependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // NOTE: Not registering IMissingDependency

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.AddFilter<FilterWithDependency>();
        });

        var act = () => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IMissingDependency*");
    }
}

// Test stubs
public sealed class TestFilter : ConsumeFilter
{
    public static bool ExecutingCalled { get; set; }
    public static bool ExecutedCalled { get; set; }

    public override Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        ExecutingCalled = true;
        return Task.CompletedTask;
    }

    public override Task OnSubscribeExecutedAsync(ExecutedContext context)
    {
        ExecutedCalled = true;
        return Task.CompletedTask;
    }
}

public sealed class AnotherTestFilter : ConsumeFilter { }

public sealed class FilterWithDependency(IMissingDependency dependency) : ConsumeFilter { }
```

### Integration Tests

**FilterIntegrationTest.cs:**

```csharp
public class FilterIntegrationTest
{
    [Fact]
    public async Task should_execute_filter_pipeline()
    {
        // Smoke test: Verify filters are invoked by CAP
        // Trust CAP for ordering, exception handling, lifecycle

        var messageProcessed = new TaskCompletionSource<bool>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(messageProcessed);

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.AddFilter<TrackingFilter>();
            m.AddConsumer<TrackingHandler>();
        });

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<ICapPublisher>();

        // Reset tracking state
        TrackingFilter.ExecutingCalled = false;
        TrackingFilter.ExecutedCalled = false;
        TrackingHandler.Called = false;

        await publisher.PublishAsync("test.topic", new TestMessage());

        // Wait for message processing with timeout (no flaky Task.Delay!)
        var completed = await messageProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue("message should process within 5s");

        // Verify filter was invoked
        TrackingFilter.ExecutingCalled.Should().BeTrue();
        TrackingFilter.ExecutedCalled.Should().BeTrue();
        TrackingHandler.Called.Should().BeTrue();
    }
}

// Test infrastructure
public sealed class TrackingFilter : ConsumeFilter
{
    public static bool ExecutingCalled { get; set; }
    public static bool ExecutedCalled { get; set; }

    public override Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        ExecutingCalled = true;
        return Task.CompletedTask;
    }

    public override Task OnSubscribeExecutedAsync(ExecutedContext context)
    {
        ExecutedCalled = true;
        return Task.CompletedTask;
    }
}

public sealed class TrackingHandler(TaskCompletionSource<bool> completion) : IConsume<TestMessage>
{
    public static bool Called { get; set; }

    public Task ConsumeAsync(TestMessage message, CancellationToken cancellationToken)
    {
        Called = true;
        completion.TrySetResult(true);
        return Task.CompletedTask;
    }
}

public record TestMessage;
```

**Test strategy:**
- **Unit tests**: Verify wrapper correctly registers filters with CAP and DI
- **Integration tests**: Single smoke test verifying CAP invokes filters (trust CAP for ordering/exception handling)
- **No flaky delays**: Use `TaskCompletionSource` for proper async synchronization

## Documentation Examples

**Example filters for README/documentation** (not in test code):

### Logging Filter

```csharp
public sealed class LoggingFilter(ILogger<LoggingFilter> logger) : ConsumeFilter
{
    public override Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        logger.LogInformation(
            "Processing message {MessageId} from topic {Topic}",
            context.Message.GetId(),
            context.Message.GetName());

        return Task.CompletedTask;
    }

    public override Task OnSubscribeExecutedAsync(ExecutedContext context)
    {
        logger.LogInformation(
            "Completed message {MessageId}",
            context.Message.GetId());

        return Task.CompletedTask;
    }

    public override Task OnSubscribeExceptionAsync(ExceptionContext context)
    {
        logger.LogError(
            context.Exception,
            "Failed to process message {MessageId}",
            context.Message.GetId());

        return Task.CompletedTask;
    }
}
```

### Metrics Filter (Optimized)

```csharp
public sealed class MetricsFilter(IMetrics metrics) : ConsumeFilter
{
    private const string TimestampKey = "MetricsFilter.StartTimestamp";

    public override Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        // Use Stopwatch.GetTimestamp() to avoid allocation
        context.Items[TimestampKey] = Stopwatch.GetTimestamp();
        metrics.IncrementCounter("messages.received");
        return Task.CompletedTask;
    }

    public override Task OnSubscribeExecutedAsync(ExecutedContext context)
    {
        if (context.Items.TryGetValue(TimestampKey, out var startObj) && startObj is long start)
        {
            var elapsed = Stopwatch.GetElapsedTime(start);
            metrics.RecordHistogram("messages.duration_ms", elapsed.TotalMilliseconds);
        }
        else
        {
            // Defensive: Log if timestamp missing (indicates filter pipeline issue)
            // Don't throw - metrics failure shouldn't break message processing
        }

        metrics.IncrementCounter("messages.success");
        return Task.CompletedTask;
    }

    public override Task OnSubscribeExceptionAsync(ExceptionContext context)
    {
        metrics.IncrementCounter("messages.failed");
        return Task.CompletedTask;
    }
}
```

### Validation Filter

```csharp
public sealed class ValidationFilter(IValidator validator) : ConsumeFilter
{
    public override async Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        var message = context.Arguments.FirstOrDefault()
            ?? throw new InvalidOperationException("No message in context - this indicates a CAP pipeline issue");

        var validationResult = await validator.ValidateAsync(message, context.CancellationToken)
            .ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                $"Message validation failed: {string.Join(", ", validationResult.Errors)}");
        }
    }
}
```

**Note**: Validation exceptions trigger CAP's retry/DLQ logic per Part 3 configuration.

## Acceptance Criteria

- [ ] `AddFilter<T>()` registers filter with CAP's `SubscriberFilters`
- [ ] Filters registered in DI as scoped (new instance per message scope)
- [ ] CAP behaviors verified (insertion order, cancellation token, exception handling, scoped lifetime)
- [ ] Comprehensive XML documentation on public API
- [ ] Integration test uses proper synchronization (no `Task.Delay`)
- [ ] Unit tests cover: basic registration, multiple filters, duplicate registration, missing dependencies
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Coverage ≥85%

## Usage Example

```csharp
services.AddMessaging(messaging =>
{
    // CAP transport configuration
    messaging.ConfigureCap(cap =>
    {
        cap.UseRabbitMQ(options => { /* ... */ });
        cap.UseSqlServer(options => { /* ... */ });
    });

    // Global filters (wraps CAP.SubscriberFilters)
    messaging.AddFilter<LoggingFilter>();
    messaging.AddFilter<MetricsFilter>();
    messaging.AddFilter<ValidationFilter>();

    // Register consumers (all get the global filters)
    messaging.AddConsumer<OrderCreatedHandler>();
    messaging.AddConsumer<PaymentReceivedHandler>();
});
```

## What CAP Provides (We Don't Reimplement)

**CAP's Built-in Filter Pipeline:**
- `ISubscribeFilter` interface (our `IConsumeFilter` extends this)
- Execution order: filters execute in registration order
- Pre-execution: `OnSubscribeExecutingAsync` before handler
- Post-execution: `OnSubscribeExecutedAsync` after handler (reverse order)
- Exception handling: `OnSubscribeExceptionAsync` on error (reverse order)
- Scoped DI: Filters resolved per message

**What We DON'T Build:**
- ❌ Custom `FilterPipeline` class (CAP already has this)
- ❌ Per-consumer filter registration (CAP only supports global)
- ❌ Filter ordering control (CAP uses registration order)
- ❌ Complex filter metadata (keep it simple)
- ❌ Filter execution logic (trust CAP)

## Files Changed

**Created:**
- `tests/Framework.Messages.Core.Tests.Unit/FilterConfigurationTest.cs` (~80 LOC)
- `tests/Framework.Messages.Core.Tests.Integration/FilterIntegrationTest.cs` (~40 LOC)
- `src/Framework.Messages.Core/README.md` - Add filter examples section (~60 LOC)

**Modified:**
- `src/Framework.Messages.Abstractions/IMessagingBuilder.cs` - Add `AddFilter<T>()` with XML docs (~30 LOC)
- `src/Framework.Messages.Core/MessagingBuilder.cs` - Add filter registration (~20 LOC)

**NOT Created** (removed from original plan):
- ❌ `FilterPipeline.cs` - Using CAP's pipeline
- ❌ Per-consumer filter support - CAP doesn't support this
- ❌ Filter metadata storage - Not needed
- ❌ Complex filter configurator - Keep simple
- ❌ Non-generic `AddFilter(Type)` overload - YAGNI
- ❌ Example filters in test projects - Moved to README

**Total Production LOC**: ~50 lines
**Total Test LOC**: ~120 lines
**Total Documentation LOC**: ~60 lines

## Why This Approach is Better

**Pragmatic**:
- CAP already has filter pipeline - don't reimplement it
- Global filters cover 95% of use cases
- Per-consumer filters add complexity for little value
- Wrapper adds ~50 LOC vs 250 LOC for custom pipeline

**Correct**:
- No async/await correctness issues (CAP handles it)
- No filter exception handling bugs (CAP handles it)
- No thread safety issues (CAP handles scoping)
- Proper XML documentation explains behavior
- Tests use proper synchronization (no flaky delays)

**Simple**:
- Single responsibility: register filters with CAP
- No custom pipeline to maintain
- Minimal test surface (trust CAP for complex behavior)
- Example filters in docs, not test code

## Limitations (By Design)

**CAP only supports global filters** - No per-consumer filters:
- Workaround: Use conditional logic inside filter based on message type
- Rationale: Most apps need consistent filtering across all consumers

**CAP executes filters in registration order** - No custom ordering:
- Workaround: Register filters in desired order
- Rationale: Registration order is explicit and predictable

**CAP filters are scoped per message** - Cannot be singleton:
- Benefit: Thread-safe by default, no shared state
- Rationale: Scoped lifetime is safer and more predictable

## Estimated Effort

**Updated Part 4**: 0.3 day (2.5 hours)

**Breakdown**:
- 15 min: Verify CAP behaviors (source code review)
- 30 min: Implement `AddFilter<T>()` with XML docs
- 20 min: Integrate with `ApplyToCapOptions()` + error handling
- 45 min: Unit tests (registration, duplicates, missing deps)
- 30 min: Integration test (smoke test with proper sync)
- 20 min: Update README with example filters

**Total**: 2.5 hours (up from 2.0 hours due to better testing + documentation)

## Summary

**What we're building:**
- `AddFilter<T>()` method wrapping `capOptions.SubscriberFilters.Add<T>()`
- DI registration for filters (scoped lifetime)
- Comprehensive XML documentation
- Simple, clean API

**What we're NOT building:**
- Custom filter pipeline
- Per-consumer filter support
- Non-generic `AddFilter(Type)` overload
- Filter metadata storage
- Complex filter configurator

**Why this is better:**
- Minimal code (50 LOC production + 120 LOC tests)
- CAP's filter pipeline is battle-tested
- Proper documentation explains behavior
- Tests verify wrapper, trust CAP for complex scenarios
- Pragmatic over perfect
