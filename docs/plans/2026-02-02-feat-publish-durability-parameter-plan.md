# feat: Add IDirectPublisher Interface for Fire-and-Forget Messaging

## Review Summary

**Reviewed on:** 2026-02-02
**Reviewers:** 3 (strict-dotnet-reviewer, pragmatic-dotnet-reviewer, code-simplicity-reviewer)

### Design Decisions

1. **Separate `IDirectPublisher` interface** - ISP compliance, constructor clarity
2. **Topic mapping only** - No explicit topic parameter; topic resolved from message type
3. **Non-nullable message** - `T contentObj` not `T?` since we need the type for topic resolution

### Rationale for Topic Mapping Only

| Benefit | Description |
|---------|-------------|
| **Type Safety** | Can't accidentally publish to wrong topic |
| **Consistency** | One message type = one topic across the system |
| **Discoverability** | All topics defined in startup configuration |
| **Refactoring** | Change topic in one place, not scattered strings |
| **Consumer Clarity** | Consumers know exactly what type to expect on each topic |

### Known Limitations (Accepted)

- `CancellationToken` not passed to `ITransport.SendAsync` (pre-existing design debt)
- Must define message class for each topic (no anonymous types)

---

## Overview

Add a new `IDirectPublisher` interface that sends messages directly to the transport without persisting to the outbox. Topics are resolved from message type mappings - no explicit topic parameter.

## Problem Statement / Motivation

The current architecture only supports durable publishing through the outbox:

```
IOutboxPublisher → IDataStorage → IDispatcher → ITransport
```

**Pain points:**

1. **No fire-and-forget option** - All messages incur database round-trip overhead
2. **Performance overhead for non-critical messages** - Metrics/telemetry don't need durability
3. **No direct access to transport** - `ITransport` is internal

**Proposed solution:**

Two focused interfaces with consistent topic-mapping API:

```csharp
// Startup - define topics for all message types
services.AddMessaging(options =>
{
    options.WithTopicMapping<OrderCreated>("orders.created");      // durable
    options.WithTopicMapping<PageViewMetric>("metrics.pageview");  // fire-and-forget
});

// Publishing - same pattern, different durability
await outboxPublisher.PublishAsync(new OrderCreated(...));   // durable
await directPublisher.PublishAsync(new PageViewMetric(...)); // fire-and-forget
```

---

## Proposed Solution

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        User Code                                 │
├────────────────────────────────┬────────────────────────────────┤
│       IOutboxPublisher         │       IDirectPublisher          │
│   (durable, at-least-once)     │   (fire-and-forget)            │
├────────────────────────────────┼────────────────────────────────┤
│  IDataStorage → IDispatcher    │        (bypassed)              │
├────────────────────────────────┴────────────────────────────────┤
│                    TopicMappings (shared)                        │
├──────────────────────────────────────────────────────────────────┤
│                        ISerializer                               │
├──────────────────────────────────────────────────────────────────┤
│                         ITransport                               │
└──────────────────────────────────────────────────────────────────┘
```

### Core Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Topic resolution** | From type mapping only | Type safety, consistency, discoverability |
| **Message parameter** | `T contentObj` (non-nullable) | Need type for topic resolution |
| **Method count** | 2 methods | With/without custom headers |
| **Delayed publishing** | Not supported | Requires persistence |

### API Consistency

Both interfaces use the same topic-mapping pattern:

```csharp
// IOutboxPublisher (existing)
Task PublishAsync<T>(T? contentObj, CancellationToken ct = default) where T : class;

// IDirectPublisher (new) - same signature pattern
Task PublishAsync<T>(T contentObj, CancellationToken ct = default) where T : class;
```

---

## Technical Approach

### New Interface: IDirectPublisher

```csharp
// Headless.Messaging.Abstractions/IDirectPublisher.cs

namespace Headless.Messaging;

/// <summary>
/// Publishes messages directly to the transport without persistence.
/// <para>
/// <b>Fire-and-forget semantics:</b> Messages are sent immediately to the transport
/// without storing in the outbox. No retries, no transaction support, no delayed delivery.
/// </para>
/// <para>
/// Topics are resolved from message type mappings configured via
/// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/>.
/// </para>
/// <para>
/// <b>Use cases:</b> Metrics, telemetry, real-time notifications, cache invalidation -
/// scenarios where occasional message loss is acceptable.
/// </para>
/// <para>
/// For reliable delivery with at-least-once guarantees, use <see cref="IOutboxPublisher"/>.
/// </para>
/// </summary>
public interface IDirectPublisher
{
    /// <summary>
    /// Publishes a message directly to the transport without persistence.
    /// The topic is resolved from the message type's configured topic mapping.
    /// </summary>
    /// <typeparam name="T">The message type. Must have a registered topic mapping.</typeparam>
    /// <param name="contentObj">The message content to serialize and publish.</param>
    /// <param name="cancellationToken">
    /// Cancellation token. Note: Cannot cancel in-flight transport sends.
    /// </param>
    /// <returns>A task that completes when the message is sent to the transport.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contentObj"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no topic mapping exists for type <typeparamref name="T"/>.</exception>
    /// <exception cref="PublisherSentFailedException">Thrown when the transport fails to send.</exception>
    /// <remarks>
    /// <b>WARNING:</b> Messages may be lost if:
    /// <list type="bullet">
    /// <item>The application crashes before the broker acknowledges receipt</item>
    /// <item>The transport is temporarily unavailable</item>
    /// <item>Network issues occur during transmission</item>
    /// </list>
    /// </remarks>
    Task PublishAsync<T>(T contentObj, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Publishes a message with custom headers directly to the transport without persistence.
    /// The topic is resolved from the message type's configured topic mapping.
    /// </summary>
    /// <typeparam name="T">The message type. Must have a registered topic mapping.</typeparam>
    /// <param name="contentObj">The message content to serialize and publish.</param>
    /// <param name="headers">Custom headers to include with the message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the message is sent to the transport.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contentObj"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no topic mapping exists for type <typeparamref name="T"/>.</exception>
    /// <exception cref="PublisherSentFailedException">Thrown when the transport fails to send.</exception>
    Task PublishAsync<T>(
        T contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    ) where T : class;
}
```

### Implementation: DirectPublisher

```csharp
// Headless.Messaging.Core/Internal/DirectPublisher.cs

namespace Headless.Messaging.Internal;

internal sealed class DirectPublisher(IServiceProvider serviceProvider) : IDirectPublisher
{
    private readonly ISerializer _serializer = serviceProvider.GetRequiredService<ISerializer>();
    private readonly ITransport _transport = serviceProvider.GetRequiredService<ITransport>();
    private readonly ILongIdGenerator _idGenerator = serviceProvider.GetRequiredService<ILongIdGenerator>();
    private readonly TimeProvider _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
    private readonly MessagingOptions _options = serviceProvider
        .GetRequiredService<IOptions<MessagingOptions>>().Value;

    public Task PublishAsync<T>(T contentObj, CancellationToken cancellationToken = default)
        where T : class
    {
        return PublishAsync(contentObj, new Dictionary<string, string?>(StringComparer.Ordinal), cancellationToken);
    }

    public async Task PublishAsync<T>(
        T contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    ) where T : class
    {
        Argument.IsNotNull(contentObj);

        cancellationToken.ThrowIfCancellationRequested();

        // Resolve topic from type mapping
        var name = _GetTopicName<T>();

        // Apply topic prefix
        if (!string.IsNullOrEmpty(_options.TopicNamePrefix))
        {
            name = $"{_options.TopicNamePrefix}.{name}";
        }

        // Generate standard headers
        _GenerateHeaders(headers, name);

        var message = new Message(headers, contentObj);

        await _SendAsync(message).AnyContext();
    }

    private string _GetTopicName<T>() where T : class
    {
        var messageType = typeof(T);

        // Check explicit topic mappings first
        if (_options.TopicMappings.TryGetValue(messageType, out var topicName))
        {
            return topicName;
        }

        // Check conventions
        if (_options.Conventions?.GetTopicName(messageType) is { } conventionTopic)
        {
            return conventionTopic;
        }

        throw new InvalidOperationException(
            $"No topic mapping found for message type '{messageType.Name}'. " +
            $"Register a topic mapping using WithTopicMapping<{messageType.Name}>(\"topic-name\") " +
            "in your messaging configuration."
        );
    }

    private async Task _SendAsync(Message message)
    {
        TransportMessage transportMsg;
        try
        {
            transportMsg = await _serializer.SerializeToTransportMessageAsync(message).AnyContext();
        }
        catch (Exception e)
        {
            _TracingErrorSerialization(message, e);
            throw;
        }

        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBeforeSend(transportMsg);

            var result = await _transport.SendAsync(transportMsg).AnyContext();

            if (!result.Succeeded)
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, result);
                throw new PublisherSentFailedException(result.ToString(), result.Exception);
            }

            _TracingAfterSend(tracingTimestamp, transportMsg);
        }
        catch (Exception e) when (e is not PublisherSentFailedException)
        {
            try
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, e);
            }
            catch
            {
                // Tracing failure should not mask the original exception
            }
            throw;
        }
    }

    private void _GenerateHeaders(IDictionary<string, string?> headers, string name)
    {
        if (!headers.TryGetValue(Headers.MessageId, out var messageId))
        {
            messageId = _idGenerator.Create().ToString(CultureInfo.InvariantCulture);
            headers[Headers.MessageId] = messageId;
        }

        if (!headers.ContainsKey(Headers.CorrelationId))
        {
            headers[Headers.CorrelationId] = messageId;
            headers[Headers.CorrelationSequence] = "0";
        }

        headers[Headers.MessageName] = name;
        headers[Headers.SentTime] = _timeProvider.GetUtcNow().UtcDateTime.ToString(CultureInfo.InvariantCulture);
    }

    #region Tracing

    private long? _TracingBeforeSend(TransportMessage message)
    {
        // DiagnosticListener.BeforePublish event
        return Stopwatch.GetTimestamp();
    }

    private void _TracingAfterSend(long? timestamp, TransportMessage message)
    {
        // DiagnosticListener.AfterPublish event
    }

    private void _TracingErrorSend(long? timestamp, TransportMessage message, OperateResult result)
    {
        // DiagnosticListener.ErrorPublish event
    }

    private void _TracingErrorSend(long? timestamp, TransportMessage message, Exception exception)
    {
        // DiagnosticListener.ErrorPublish event
    }

    private void _TracingErrorSerialization(Message message, Exception exception)
    {
        // DiagnosticListener.ErrorPublish event for serialization failures
    }

    #endregion
}
```

### DI Registration

```csharp
// Headless.Messaging.Core/ServiceCollectionExtensions.cs

public static IServiceCollection AddMessaging(this IServiceCollection services, Action<MessagingOptions> configure)
{
    // Existing registrations...
    services.TryAddScoped<IOutboxPublisher, OutboxPublisher>();

    // NEW: Register IDirectPublisher
    services.TryAddScoped<IDirectPublisher, DirectPublisher>();

    return services;
}
```

### Error Handling Contract

| Scenario | IOutboxPublisher | IDirectPublisher |
|----------|-----------------|------------------|
| Transport failure | Store as Failed, retry later | Throw `PublisherSentFailedException` |
| Serialization error | Throw `JsonException` | Throw `JsonException` |
| Null message | Throw `ArgumentNullException` | Throw `ArgumentNullException` |
| Missing topic mapping | Throw `InvalidOperationException` | Throw `InvalidOperationException` |
| Transaction active | Works (message in txn) | N/A (no transaction support) |

---

## Usage Examples

### Configuration

```csharp
// Program.cs
builder.Services.AddMessaging(options =>
{
    // Business events - use IOutboxPublisher
    options.WithTopicMapping<OrderCreated>("orders.created");
    options.WithTopicMapping<PaymentProcessed>("payments.processed");

    // Metrics - use IDirectPublisher
    options.WithTopicMapping<PageViewMetric>("metrics.pageview");
    options.WithTopicMapping<ApiLatencyMetric>("metrics.api-latency");
    options.WithTopicMapping<ErrorMetric>("metrics.errors");

    // Cache invalidation - use IDirectPublisher
    options.WithTopicMapping<CacheInvalidation>("cache.invalidate");
});
```

### Message Types

```csharp
// Metrics messages
public record PageViewMetric(string Url, string? UserId, DateTimeOffset Timestamp);
public record ApiLatencyMetric(string Endpoint, string Method, int Milliseconds);
public record ErrorMetric(string Source, string Message, string? StackTrace);

// Cache invalidation
public record CacheInvalidation(string CacheKey, string? Region = null);
```

### Publishing

```csharp
public sealed class AnalyticsService(IDirectPublisher publisher, TimeProvider time)
{
    public async Task TrackPageViewAsync(string url, string? userId)
    {
        var metric = new PageViewMetric(url, userId, time.GetUtcNow());
        await publisher.PublishAsync(metric);
        // Topic "metrics.pageview" resolved automatically
    }

    public async Task TrackApiLatencyAsync(string endpoint, string method, int ms)
    {
        await publisher.PublishAsync(new ApiLatencyMetric(endpoint, method, ms));
    }
}

public sealed class CacheService(IDirectPublisher publisher)
{
    public async Task InvalidateAsync(string key, string? region = null)
    {
        await publisher.PublishAsync(new CacheInvalidation(key, region));
    }
}
```

### Mixed Usage (Both Interfaces)

```csharp
public sealed class OrderService(
    IOutboxPublisher outboxPublisher,   // Critical business events
    IDirectPublisher directPublisher)   // Metrics
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        // Save order to database...

        // MUST NOT lose - business event
        await outboxPublisher.PublishAsync(new OrderCreated(order.Id, order.Total), ct);

        // OK to lose - just metrics
        await directPublisher.PublishAsync(new ApiLatencyMetric("/orders", "POST", 125), ct);
    }
}
```

---

## Stories

| # | Story | Size | Notes |
|---|-------|------|-------|
| 1 | Create `IDirectPublisher` interface in Abstractions | S | 2 methods with XML docs |
| 2 | Create `DirectPublisher` implementation in Core | M | Topic resolution, serialization, transport |
| 3 | Register `IDirectPublisher` in DI | XS | `TryAddScoped` |
| 4 | Unit tests for DirectPublisher | M | Topic resolution, transport error, headers |
| 5 | Integration test with InMemoryQueue | M | End-to-end verification |
| 6 | Update package README with examples | S | Usage patterns |

---

## Acceptance Criteria

### Functional Requirements

- [ ] `IDirectPublisher` interface exists in `Headless.Messaging.Abstractions`
- [ ] `DirectPublisher` implementation exists in `Headless.Messaging.Core`
- [ ] Topic resolved from `MessagingOptions.TopicMappings`
- [ ] Topic resolved from `MessagingOptions.Conventions` (fallback)
- [ ] `InvalidOperationException` thrown when no topic mapping exists
- [ ] `ArgumentNullException` thrown when message is null
- [ ] `PublisherSentFailedException` thrown on transport failure
- [ ] Standard headers generated (MessageId, CorrelationId, SentTime, MessageName)
- [ ] `TopicNamePrefix` applied when configured
- [ ] `IOutboxPublisher` unchanged (binary compatible)

### Non-Functional Requirements

- [ ] No database round-trip
- [ ] OpenTelemetry spans created for sends
- [ ] Thread-safe for concurrent publishing

### Quality Gates

- [ ] ≥85% line coverage, ≥80% branch coverage
- [ ] All existing tests pass
- [ ] Integration test with InMemoryQueue passes
- [ ] XML documentation complete
- [ ] CSharpier formatted

---

## Test Examples

```csharp
public sealed class DirectPublisherTests : TestBase
{
    [Fact]
    public async Task should_resolve_topic_from_mapping()
    {
        // Arrange
        TransportMessage? captured = null;
        var transport = Substitute.For<ITransport>();
        transport.SendAsync(Arg.Do<TransportMessage>(m => captured = m))
            .Returns(OperateResult.Success);

        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = CreateDirectPublisher(transport, options);

        // Act
        await publisher.PublishAsync(new TestMessage());

        // Assert
        captured!.Headers[Headers.MessageName].Should().Be("test.topic");
    }

    [Fact]
    public async Task should_throw_when_no_topic_mapping()
    {
        // Arrange
        var publisher = CreateDirectPublisher();

        // Act & Assert
        var act = () => publisher.PublishAsync(new UnmappedMessage());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No topic mapping found*UnmappedMessage*");
    }

    [Fact]
    public async Task should_throw_when_message_is_null()
    {
        // Arrange
        var publisher = CreateDirectPublisher();

        // Act & Assert
        var act = () => publisher.PublishAsync<TestMessage>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_apply_topic_prefix()
    {
        // Arrange
        TransportMessage? captured = null;
        var transport = Substitute.For<ITransport>();
        transport.SendAsync(Arg.Do<TransportMessage>(m => captured = m))
            .Returns(OperateResult.Success);

        var options = new MessagingOptions { TopicNamePrefix = "myapp" };
        options.TopicMappings[typeof(TestMessage)] = "events";

        var publisher = CreateDirectPublisher(transport, options);

        // Act
        await publisher.PublishAsync(new TestMessage());

        // Assert
        captured!.Headers[Headers.MessageName].Should().Be("myapp.events");
    }

    [Fact]
    public async Task should_throw_on_transport_failure()
    {
        // Arrange
        var transport = Substitute.For<ITransport>();
        transport.SendAsync(Arg.Any<TransportMessage>())
            .Returns(OperateResult.Failed(new Exception("Connection refused")));

        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = CreateDirectPublisher(transport, options);

        // Act & Assert
        var act = () => publisher.PublishAsync(new TestMessage());

        await act.Should().ThrowAsync<PublisherSentFailedException>();
    }

    [Fact]
    public async Task should_generate_standard_headers()
    {
        // Arrange
        TransportMessage? captured = null;
        var transport = Substitute.For<ITransport>();
        transport.SendAsync(Arg.Do<TransportMessage>(m => captured = m))
            .Returns(OperateResult.Success);

        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = CreateDirectPublisher(transport, options);

        // Act
        await publisher.PublishAsync(new TestMessage());

        // Assert
        captured.Should().NotBeNull();
        captured!.Headers.Should().ContainKey(Headers.MessageId);
        captured.Headers.Should().ContainKey(Headers.CorrelationId);
        captured.Headers.Should().ContainKey(Headers.SentTime);
        captured.Headers.Should().ContainKey(Headers.MessageName);
    }
}
```

---

## Dependencies & Risks

### Dependencies

- `ITransport` must be registered
- `ISerializer` must be registered
- Topic mapping must be configured for message types

### Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Message loss | Expected | Documented; interface name conveys risk |
| Missing topic mapping | LOW | Clear exception message with fix instructions |
| Requires message classes | LOW | Enforces good design; no anonymous types |

---

## Deferred Items (YAGNI)

| Item | Reason |
|------|--------|
| Explicit topic parameter | Forces type safety; all topics via mapping |
| `PublishManyAsync` batch | Add when needed; loop is fine for MVP |
| Header validation | No current security issue |
| Config kill-switch | Add if users request |

---

## References

### Internal

- `src/Headless.Messaging.Abstractions/IOutboxPublisher.cs` - Existing interface pattern
- `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs` - Topic mappings

### External

- [MassTransit Transactional Outbox](https://masstransit.io/documentation/patterns/transactional-outbox)
- [Wolverine Durable Messaging](https://wolverinefx.net/guide/durability/)

---

## Unresolved Questions

1. **CancellationToken not passed to ITransport.SendAsync** - Document limitation; consider fixing in future.
