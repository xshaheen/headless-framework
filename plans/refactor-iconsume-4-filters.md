# IConsume<T> Part 4: Filters (CAP Wrapper)

**Priority**: MEDIUM-LOW
**Dependencies**: Part 1 (Core Foundation)
**Estimated Effort**: 0.25 day

## Goal

Expose CAP's built-in subscriber filter system through a cleaner API in the `AddMessaging()` builder.

## Scope

**In Scope:**
- Wrapper API for CAP's `SubscriberFilters`
- Global filter registration
- Simple, clean configuration interface

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

## Implementation

### Phase 1: Filter Registration Wrapper

**Add to IMessagingBuilder.cs:**

```csharp
public interface IMessagingBuilder
{
    // Existing methods...

    // NEW - Wraps CAP's SubscriberFilters
    IMessagingBuilder AddFilter<TFilter>() where TFilter : class, IConsumeFilter;
    IMessagingBuilder AddFilter(Type filterType);
}
```

**Note**: `IConsumeFilter` already exists in Framework.Messages.Abstractions and extends CAP's `ISubscribeFilter`.

### Phase 2: Implementation in MessagingBuilder

**Update MessagingBuilder.cs:**

```csharp
public sealed class MessagingBuilder(IServiceCollection services) : IMessagingBuilder
{
    private readonly List<Type> _filterTypes = [];

    public IMessagingBuilder AddFilter<TFilter>() where TFilter : class, IConsumeFilter
    {
        _filterTypes.Add(typeof(TFilter));

        // Register filter in DI (scoped - new instance per message)
        services.AddScoped<TFilter>();

        return this;
    }

    public IMessagingBuilder AddFilter(Type filterType)
    {
        if (!typeof(IConsumeFilter).IsAssignableFrom(filterType))
        {
            throw new ArgumentException(
                $"{filterType.Name} must implement IConsumeFilter",
                nameof(filterType));
        }

        _filterTypes.Add(filterType);
        services.AddScoped(filterType);

        return this;
    }

    internal void ApplyToCapOptions(CapOptions capOptions)
    {
        // Apply retry/DLQ configuration (from Part 3)
        // ...

        // Apply filters
        foreach (var filterType in _filterTypes)
        {
            // CAP's SubscriberFilters.Add method
            capOptions.SubscriberFilters.Add(filterType);
        }
    }
}
```

### Phase 3: Integration with CAP Configuration

**Already integrated in Part 3** via `ApplyToCapOptions()` in `AddMessaging()`.

No additional changes needed - filters are registered alongside retry/DLQ configuration.

## Testing

### Unit Tests

```csharp
public class FilterConfigurationTest
{
    [Fact]
    public void should_register_filter_in_di()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.AddFilter<LoggingFilter>();
        });

        var sp = services.BuildServiceProvider();
        var filter = sp.GetRequiredService<LoggingFilter>();

        filter.Should().NotBeNull();
    }

    [Fact]
    public void should_add_filter_to_cap_subscriber_filters()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.AddFilter<LoggingFilter>();
            m.AddFilter<MetricsFilter>();
        });

        var capOptions = GetCapOptions(services);

        capOptions.SubscriberFilters
            .Should().Contain(typeof(LoggingFilter))
            .And.Contain(typeof(MetricsFilter));
    }

    [Fact]
    public void should_throw_when_filter_not_iconsume_filter()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
        {
            services.AddMessaging(m =>
            {
                m.AddFilter(typeof(InvalidFilter)); // Not IConsumeFilter
            });
        };

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must implement IConsumeFilter*");
    }

    [Fact]
    public void should_register_multiple_filters_in_order()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.AddFilter<Filter1>();
            m.AddFilter<Filter2>();
            m.AddFilter<Filter3>();
        });

        var capOptions = GetCapOptions(services);

        var filters = capOptions.SubscriberFilters.ToList();
        filters.IndexOf(typeof(Filter1)).Should().BeLessThan(filters.IndexOf(typeof(Filter2)));
        filters.IndexOf(typeof(Filter2)).Should().BeLessThan(filters.IndexOf(typeof(Filter3)));
    }
}
```

### Integration Tests

```csharp
public class FilterIntegrationTest
{
    [Fact]
    public async Task should_execute_filter_before_handler()
    {
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(executionOrder);

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

        await publisher.PublishAsync("test.topic", new TestMessage());
        await Task.Delay(100);

        executionOrder.Should().Equal(
            "Filter.Executing",
            "Handler",
            "Filter.Executed"
        );
    }

    [Fact]
    public async Task should_execute_multiple_filters_in_order()
    {
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(executionOrder);

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.AddFilter<Filter1>();
            m.AddFilter<Filter2>();
            m.AddConsumer<TrackingHandler>();
        });

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<ICapPublisher>();

        await publisher.PublishAsync("test.topic", new TestMessage());
        await Task.Delay(100);

        executionOrder.Should().Equal(
            "Filter1.Executing",
            "Filter2.Executing",
            "Handler",
            "Filter2.Executed",
            "Filter1.Executed"
        );
    }

    [Fact]
    public async Task should_call_exception_handler_on_failure()
    {
        var exceptionHandled = false;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Action<bool>>(handled => exceptionHandled = handled);

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.AddFilter<ExceptionHandlingFilter>();
            m.AddConsumer<FailingHandler>();
        });

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<ICapPublisher>();

        await publisher.PublishAsync("test.topic", new TestMessage());
        await Task.Delay(100);

        exceptionHandled.Should().BeTrue();
    }
}
```

## Example Filters

```csharp
// Logging filter
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

// Metrics filter
public sealed class MetricsFilter(IMetrics metrics) : ConsumeFilter
{
    private const string StopwatchKey = "MetricsFilter.Stopwatch";

    public override Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        context.Items[StopwatchKey] = stopwatch;

        metrics.IncrementCounter("messages.received");
        return Task.CompletedTask;
    }

    public override Task OnSubscribeExecutedAsync(ExecutedContext context)
    {
        if (context.Items.TryGetValue(StopwatchKey, out var obj) && obj is Stopwatch sw)
        {
            sw.Stop();
            metrics.RecordHistogram("messages.duration_ms", sw.ElapsedMilliseconds);
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

// Validation filter
public sealed class ValidationFilter(IValidator validator) : ConsumeFilter
{
    public override async Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        // Extract message from context
        var message = context.Arguments.FirstOrDefault();
        if (message == null) return;

        var validationResult = await validator.ValidateAsync(message);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(
                $"Message validation failed: {string.Join(", ", validationResult.Errors)}");
        }
    }
}
```

## Acceptance Criteria

- [ ] `AddFilter<T>()` registers filter with CAP's `SubscriberFilters`
- [ ] Filters registered in DI as scoped (new instance per message)
- [ ] Multiple filters execute in registration order
- [ ] CAP handles filter pipeline execution (no custom pipeline)
- [ ] Integration tests verify filter execution
- [ ] Unit tests pass
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
- `tests/Framework.Messages.Core.Tests.Unit/FilterConfigurationTest.cs`
- `tests/Framework.Messages.Core.Tests.Integration/FilterIntegrationTest.cs`
- `tests/Framework.Messages.Core.Tests.Unit/Filters/LoggingFilter.cs` (example)
- `tests/Framework.Messages.Core.Tests.Unit/Filters/MetricsFilter.cs` (example)

**Modified:**
- `src/Framework.Messages.Abstractions/IMessagingBuilder.cs` (add AddFilter methods)
- `src/Framework.Messages.Core/MessagingBuilder.cs` (add filter registration)

**NOT Created** (vs original plan):
- ❌ `FilterPipeline.cs` - Using CAP's pipeline
- ❌ Per-consumer filter support - CAP doesn't support this
- ❌ Filter metadata storage - Not needed
- ❌ Complex filter configurator - Keep simple

**Total LOC**: ~50 lines (vs 250 lines in original plan)

## Comparison: Before vs After

### Before (Reimplementation)

```csharp
// Custom filter pipeline
internal sealed class FilterPipeline(IServiceProvider serviceProvider)
{
    public async Task ExecuteAsync(
        IEnumerable<Type> filterTypes,
        Func<Task> next,
        ExecutingContext executingContext,
        ExecutedContext executedContext,
        ExceptionContext? exceptionContext = null)
    {
        // 80 LOC of custom filter execution logic
    }
}

// Per-consumer filters
messaging.AddConsumer<PaymentHandler>(c =>
{
    c.AddFilter<ValidationFilter>();  // Can't do this with CAP
});
```

### After (CAP Wrapper)

```csharp
// Simple wrapper over CAP.SubscriberFilters
messaging.AddFilter<LoggingFilter>();  // → capOptions.SubscriberFilters.Add<LoggingFilter>()

// ~50 LOC wrapper code, CAP does the work
```

## Why This Approach is Better

**Pragmatic** (per Scott Hanselman review):
- CAP already has filter pipeline - don't reimplement it
- Global filters cover 95% of use cases
- Per-consumer filters add complexity for little value

**Correct** (per Stephen Toub review):
- No async/await correctness issues (CAP handles it)
- No filter exception handling bugs (CAP handles it)
- No thread safety issues (CAP handles scoping)

**Simple** (per Simplicity review):
- 80% less code (50 LOC vs 250 LOC)
- Single responsibility: register filters with CAP
- No custom pipeline to maintain

## Limitations (By Design)

**CAP only supports global filters** - No per-consumer filters:
- Workaround: Use conditional logic inside filter based on message type
- Rationale: Most apps need consistent filtering across all consumers

**Example - Per-consumer behavior in global filter:**
```csharp
public sealed class ConditionalValidationFilter(IValidator validator) : ConsumeFilter
{
    public override async Task OnSubscribeExecutingAsync(ExecutingContext context)
    {
        var message = context.Arguments.FirstOrDefault();
        if (message == null) return;

        // Only validate payment messages
        if (message.GetType().Name.Contains("Payment"))
        {
            var result = await validator.ValidateAsync(message);
            if (!result.IsValid)
                throw new ValidationException("Validation failed");
        }
    }
}
```

**CAP executes filters in registration order** - No custom ordering:
- Workaround: Register filters in desired order
- Rationale: Registration order is explicit and predictable

**CAP filters are scoped per message** - Cannot be singleton:
- Benefit: Thread-safe by default, no shared state
- Rationale: Scoped lifetime is safer and more predictable

## Migration from Original Part 4

**Original plan**: Custom filter pipeline with per-consumer filters
**Updated plan**: Thin wrapper over CAP's `SubscriberFilters`

**Breaking change**: No per-consumer filters (use conditional logic in global filters instead)

## Estimated Effort

**Original Part 4**: 1 day
**Updated Part 4**: 0.25 day (2 hours)

**Breakdown**:
- 30 min: Implement `AddFilter()` methods
- 30 min: Integrate with `ApplyToCapOptions()`
- 30 min: Unit tests
- 30 min: Integration tests

**Savings**: 0.75 day (6 hours)

## Summary

**What we're building:**
- `AddFilter<T>()` method that wraps `capOptions.SubscriberFilters.Add<T>()`
- DI registration for filters (scoped lifetime)
- Simple, clean API

**What we're NOT building:**
- Custom filter pipeline
- Per-consumer filter support
- Filter metadata storage
- Complex filter configurator

**Why this is better:**
- 5x less code (50 LOC vs 250 LOC)
- CAP's filter pipeline is battle-tested
- No custom async/await logic to maintain
- Pragmatic over perfect
