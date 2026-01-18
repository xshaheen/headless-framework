# IConsume<T> Part 3: Retry & Error Handling (CAP Wrapper)

**Priority**: MEDIUM
**Dependencies**: Part 1 (Core Foundation)
**Estimated Effort**: 0.5 day

## Goal

Expose CAP's built-in retry and dead letter queue functionality through a cleaner API in the `AddMessaging()` builder.

## Scope

**In Scope:**
- Wrapper API for CAP's retry configuration (`FailedRetryCount`, `FailedRetryInterval`)
- Wrapper API for CAP's DLQ (`FailedThresholdCallback`)
- Simple, clean configuration interface

**Out of Scope:**
- Custom retry policy implementation (CAP already has this)
- Custom backoff strategies (CAP uses fixed interval)
- Exception type filtering (CAP retries all failures)
- Custom retry logic (trust CAP's implementation)

## Architecture Decision

**DON'T reimplement retry logic**. CAP already provides:
- Automatic retry with configurable max retries
- Fixed retry interval
- Dead letter queue via `FailedThresholdCallback`

**DO provide cleaner API** over CAP's configuration to match our builder pattern.

## Implementation

### Phase 1: Retry Configuration Wrapper

**Add to IMessagingBuilder.cs:**

```csharp
public interface IMessagingBuilder
{
    // Existing methods...

    // NEW - Wraps CAP retry configuration
    IMessagingBuilder ConfigureRetry(Action<IRetryConfigurator> configure);
    IMessagingBuilder ConfigureDeadLetterQueue(Action<IDeadLetterQueueConfigurator> configure);
}

public interface IRetryConfigurator
{
    /// <summary>Maps to CAP's FailedRetryCount</summary>
    void MaxRetries(int count);

    /// <summary>Maps to CAP's FailedRetryInterval (seconds)</summary>
    void RetryInterval(TimeSpan interval);
}

public interface IDeadLetterQueueConfigurator
{
    /// <summary>Topic to send failed messages to</summary>
    void Topic(string topic);
}
```

### Phase 2: Implementation in MessagingBuilder

**Update MessagingBuilder.cs:**

```csharp
public sealed class MessagingBuilder(IServiceCollection services) : IMessagingBuilder
{
    private int? _maxRetries;
    private TimeSpan? _retryInterval;
    private string? _dlqTopic;

    public IMessagingBuilder ConfigureRetry(Action<IRetryConfigurator> configure)
    {
        var configurator = new RetryConfigurator();
        configure(configurator);

        _maxRetries = configurator.MaxRetries;
        _retryInterval = configurator.RetryInterval;

        return this;
    }

    public IMessagingBuilder ConfigureDeadLetterQueue(Action<IDeadLetterQueueConfigurator> configure)
    {
        var configurator = new DeadLetterQueueConfigurator();
        configure(configurator);

        _dlqTopic = configurator.Topic;

        return this;
    }

    internal void ApplyToCapOptions(CapOptions capOptions)
    {
        // Apply retry configuration
        if (_maxRetries.HasValue)
        {
            capOptions.FailedRetryCount = _maxRetries.Value;
        }

        if (_retryInterval.HasValue)
        {
            capOptions.FailedRetryInterval = (int)_retryInterval.Value.TotalSeconds;
        }

        // Apply DLQ configuration
        if (_dlqTopic != null)
        {
            capOptions.FailedThresholdCallback = async failedMessage =>
            {
                var publisher = failedMessage.ServiceProvider.GetRequiredService<ICapPublisher>();

                // Add metadata headers
                var headers = new Dictionary<string, string?>
                {
                    ["OriginalTopic"] = failedMessage.Message.GetName(),
                    ["FailedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["RetryCount"] = failedMessage.Message.Retries.ToString()
                };

                await publisher.PublishAsync(_dlqTopic, failedMessage.Message.Value, headers);
            };
        }
    }
}

// Simple configurators (no complex logic)
internal sealed class RetryConfigurator : IRetryConfigurator
{
    internal int? MaxRetries { get; private set; }
    internal TimeSpan? RetryInterval { get; private set; }

    public void MaxRetries(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Max retries must be >= 0");

        MaxRetries = count;
    }

    public void RetryInterval(TimeSpan interval)
    {
        if (interval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "Retry interval must be positive");

        RetryInterval = interval;
    }
}

internal sealed class DeadLetterQueueConfigurator : IDeadLetterQueueConfigurator
{
    internal string? Topic { get; private set; }

    public void Topic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("DLQ topic cannot be empty", nameof(topic));

        Topic = topic;
    }
}
```

### Phase 3: Integration with CAP Configuration

**Update MessagingBuilderExtensions.cs:**

```csharp
public static IServiceCollection AddMessaging(
    this IServiceCollection services,
    Action<IMessagingBuilder> configure)
{
    var builder = new MessagingBuilder(services);
    configure(builder);

    // Add CAP with our configuration applied
    services.AddCap(capOptions =>
    {
        // Apply retry/DLQ settings from builder
        builder.ApplyToCapOptions(capOptions);

        // User's CAP configuration (transport, storage)
        builder.ApplyCapConfiguration(capOptions);
    });

    builder.FinalizeConfiguration();
    return services;
}
```

**Update ConfigureCap implementation:**

```csharp
public sealed class MessagingBuilder(IServiceCollection services) : IMessagingBuilder
{
    private Action<CapOptions>? _capConfiguration;

    public IMessagingBuilder ConfigureCap(Action<CapOptions> configure)
    {
        _capConfiguration = configure;
        return this;
    }

    internal void ApplyCapConfiguration(CapOptions capOptions)
    {
        // Apply user's CAP configuration (transport, storage, etc.)
        _capConfiguration?.Invoke(capOptions);
    }
}
```

## Testing

### Unit Tests

```csharp
public class RetryConfigurationTest
{
    [Fact]
    public void should_apply_max_retries_to_cap_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.ConfigureRetry(retry =>
            {
                retry.MaxRetries(5);
            });
        });

        var capOptions = GetCapOptions(services);
        capOptions.FailedRetryCount.Should().Be(5);
    }

    [Fact]
    public void should_apply_retry_interval_to_cap_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.ConfigureRetry(retry =>
            {
                retry.RetryInterval(TimeSpan.FromSeconds(30));
            });
        });

        var capOptions = GetCapOptions(services);
        capOptions.FailedRetryInterval.Should().Be(30);
    }

    [Fact]
    public void should_configure_dlq_callback()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessaging(m =>
        {
            m.ConfigureDeadLetterQueue(dlq =>
            {
                dlq.Topic("dead-letter");
            });
        });

        var capOptions = GetCapOptions(services);
        capOptions.FailedThresholdCallback.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_on_negative_max_retries()
    {
        var act = () =>
        {
            var configurator = new RetryConfigurator();
            configurator.MaxRetries(-1);
        };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

### Integration Tests

```csharp
public class RetryIntegrationTest
{
    [Fact]
    public async Task should_retry_failed_messages()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var attemptCount = 0;
        services.AddTransient<FailingHandler>(sp => new FailingHandler(() => attemptCount++));

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.ConfigureRetry(retry =>
            {
                retry.MaxRetries(3);
                retry.RetryInterval(TimeSpan.FromSeconds(1));
            });

            m.AddConsumer<FailingHandler>();
        });

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<ICapPublisher>();

        // Publish message that will fail
        await publisher.PublishAsync("test.topic", new TestMessage());

        // Wait for retries
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Should have tried 4 times (initial + 3 retries)
        attemptCount.Should().Be(4);
    }

    [Fact]
    public async Task should_send_to_dlq_after_max_retries()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var dlqMessages = new List<object>();
        services.AddTransient<DlqHandler>(sp => new DlqHandler(msg => dlqMessages.Add(msg)));

        services.AddMessaging(m =>
        {
            m.ConfigureCap(cap =>
            {
                cap.UseInMemoryStorage();
                cap.UseInMemoryMessageQueue();
            });

            m.ConfigureRetry(retry => retry.MaxRetries(2));

            m.ConfigureDeadLetterQueue(dlq => dlq.Topic("dead-letter"));

            m.AddConsumer<AlwaysFailingHandler>();
            m.AddConsumer<DlqHandler>(c => c.Topic("dead-letter"));
        });

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<ICapPublisher>();

        await publisher.PublishAsync("test.topic", new TestMessage());
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Should receive message in DLQ
        dlqMessages.Should().ContainSingle();
    }
}
```

## Acceptance Criteria

- [ ] `ConfigureRetry()` maps to CAP's `FailedRetryCount` and `FailedRetryInterval`
- [ ] `ConfigureDeadLetterQueue()` configures CAP's `FailedThresholdCallback`
- [ ] DLQ messages include original topic and retry count in headers
- [ ] CAP handles actual retry logic (no custom retry implementation)
- [ ] Integration tests verify retry and DLQ behavior
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

    // Retry configuration (wraps CAP.FailedRetryCount/FailedRetryInterval)
    messaging.ConfigureRetry(retry =>
    {
        retry.MaxRetries(3);                       // CAP will retry 3 times
        retry.RetryInterval(TimeSpan.FromSeconds(60)); // Wait 60s between retries
    });

    // Dead letter queue (wraps CAP.FailedThresholdCallback)
    messaging.ConfigureDeadLetterQueue(dlq =>
    {
        dlq.Topic("dead-letter");  // Send failed messages here
    });

    // Register consumers
    messaging.AddConsumer<OrderCreatedHandler>();
});
```

## What CAP Provides (We Don't Reimplement)

**CAP's Built-in Retry:**
- Automatic retry on failure
- Fixed interval between retries
- Max retry count enforcement
- Retry counter in message headers

**CAP's Built-in DLQ:**
- `FailedThresholdCallback` fires after max retries
- Access to failed message with metadata
- Can publish to any topic

**What We DON'T Build:**
- ❌ Custom retry policy abstraction
- ❌ Exponential/linear backoff strategies (CAP uses fixed interval)
- ❌ Exception type filtering (CAP retries all failures)
- ❌ Per-consumer retry overrides (CAP is global only)
- ❌ Custom retry logic (trust CAP)

## Files Changed

**Created:**
- `tests/Framework.Messages.Core.Tests.Unit/RetryConfigurationTest.cs`
- `tests/Framework.Messages.Core.Tests.Integration/RetryIntegrationTest.cs`

**Modified:**
- `src/Framework.Messages.Abstractions/IMessagingBuilder.cs` (add ConfigureRetry/ConfigureDeadLetterQueue)
- `src/Framework.Messages.Core/MessagingBuilder.cs` (add configurators and ApplyToCapOptions)
- `src/Framework.Messages.Abstractions/MessagingBuilderExtensions.cs` (integrate with AddCap)

**NOT Created** (vs original plan):
- ❌ `RetryPolicy.cs` - Using CAP's retry
- ❌ `RetryBackoffStrategy.cs` - Using CAP's fixed interval
- ❌ `ExponentialBackoff.cs` - Using CAP's retry
- ❌ `LinearBackoff.cs` - Using CAP's retry
- ❌ `RetryPolicyConfigurator.cs` - Simplified inline
- ❌ `DeadLetterQueueConfig.cs` - Simplified inline

**Total LOC**: ~150 lines (vs 600 lines in original plan)

## Comparison: Before vs After

### Before (Reimplementation)

```csharp
// Custom retry policy with backoff strategies
messaging.ConfigureRetryPolicy(retry =>
{
    retry.MaxRetries(3);
    retry.BackoffExponential(TimeSpan.FromSeconds(1));
    retry.RetryOn<TimeoutException>();
    retry.DoNotRetryOn<ValidationException>();
});

// 600 LOC of custom retry logic
```

### After (CAP Wrapper)

```csharp
// Simple wrapper over CAP configuration
messaging.ConfigureRetry(retry =>
{
    retry.MaxRetries(3);  // → cap.FailedRetryCount = 3
    retry.RetryInterval(TimeSpan.FromSeconds(60));  // → cap.FailedRetryInterval = 60
});

// ~150 LOC wrapper code, CAP does the work
```

## Why This Approach is Better

**Pragmatic** (per Scott Hanselman review):
- CAP already has retry logic - don't reimplement it
- Fixed interval is sufficient for most use cases
- If you need exponential backoff, implement in handler

**Correct** (per Stephen Toub review):
- No async/await correctness issues (CAP handles it)
- No CancellationToken bugs (CAP handles it)
- No race conditions (CAP handles it)

**Simple** (per Simplicity review):
- 75% less code (150 LOC vs 600 LOC)
- Single responsibility: expose CAP's config
- No custom backoff math or exception filtering

## Limitations (By Design)

**CAP uses fixed retry interval** - No exponential backoff:
- Workaround: If needed, implement backoff in handler logic
- Rationale: 95% of cases don't need exponential backoff

**CAP retries all exceptions** - No exception type filtering:
- Workaround: Catch non-retryable exceptions in handler
- Rationale: Handler knows which errors are retryable

**CAP retry is global only** - No per-consumer overrides:
- Workaround: Use multiple CAP configurations (advanced)
- Rationale: Most apps need consistent retry behavior

## Migration from Original Part 3

**Original plan**: Custom retry framework with backoff strategies
**Updated plan**: Thin wrapper over CAP's retry

**No API breaking changes** - Just different implementation underneath.

## Estimated Effort

**Original Part 3**: 1-2 days
**Updated Part 3**: 0.5 day (4 hours)

**Breakdown**:
- 1 hour: Implement configurators
- 1 hour: Integrate with CAP options
- 1 hour: Unit tests
- 1 hour: Integration tests

**Savings**: 0.5-1.5 days
