# feat: Add ITransportPublisher for Direct Transport Publishing

## Overview

Add `ITransportPublisher` interface to `Headless.Messaging.Abstractions` for publishing messages directly to the transport, bypassing the outbox persistence layer. This enables fire-and-forget messaging patterns where durability isn't critical (logs, metrics, notifications).

## Problem Statement / Motivation

Currently, all message publishing in `Headless.Messaging` goes through the outbox pattern:

```
IOutboxPublisher → IDataStorage (persist) → IDispatcher (queue) → IMessageSender → ITransport
```

While this provides excellent durability and transactional guarantees, it adds overhead that isn't needed for all use cases:

1. **Fire-and-forget messages** (metrics, telemetry, non-critical notifications) don't need persistence
2. **Performance-sensitive paths** may not tolerate outbox storage latency
3. **High-volume scenarios** (logging millions of events) create unnecessary database load

## Proposed Solution

Add a new `ITransportPublisher` interface that publishes directly to `ITransport`:

```
ITransportPublisher → ISerializer → ITransport
```

Key design decisions:
- **Separate interface** - Clean separation from `IOutboxPublisher`, explicit intent
- **Auto-generate headers** - Consistent behavior with outbox path (MessageId, CorrelationId, SentTime, Type)
- **Include batch API** - `PublishManyAsync` for efficient bulk publishing
- **No retries** - True fire-and-forget semantics
- **Share configuration** - Use same `TopicMappings` and `TopicNamePrefix` from `MessagingOptions`

## Technical Considerations

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     User Code                                    │
├─────────────────────────────────────────────────────────────────┤
│  IOutboxPublisher (durable)  │  ITransportPublisher (direct)    │
├──────────────────────────────┼──────────────────────────────────┤
│  IDataStorage → IDispatcher  │         (bypassed)               │
├──────────────────────────────┴──────────────────────────────────┤
│                     ISerializer                                  │
├─────────────────────────────────────────────────────────────────┤
│                      ITransport                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Header Generation

Mirror `OutboxPublisher._PublishInternalAsync` behavior:
- `MessageId` - Auto-generated via `ILongIdGenerator`
- `CorrelationId` - Set to MessageId if not provided
- `CorrelationSequence` - Set to 0 if not provided
- `MessageName` - Topic name (with prefix if configured)
- `Type` - `typeof(T).Name`
- `SentTime` - Current UTC time
- User-provided headers merged (user values take precedence for non-reserved keys)

### Error Handling Contract

- **Transport errors** → Return `OperateResult.Failed(exception)` (no throw)
- **Serialization errors** → Throw `JsonException` (programming error)
- **Null/empty topic name** → Throw `ArgumentNullException` (programming error)
- **Missing topic mapping** → Throw `InvalidOperationException` (programming error)

### Performance

- **No database round-trip** - Direct to transport
- **No retry logic** - Single send attempt
- **Batch API** - Single serialization pass, single transport call where supported
- **Singleton lifetime** - Share transport connection pool

### Observability

- **DiagnosticListener integration** - Use existing `MessageDiagnosticListenerNames.BeforePublish`/`AfterPublish`/`ErrorPublish`
- **OpenTelemetry spans** - Create spans for distributed tracing
- **Logging** - Minimal (avoid spam for high-volume scenarios)

## Stories

| # | Story | Size | Notes |
|---|-------|------|-------|
| 1 | Create `ITransportPublisher` interface in Abstractions | S | Core API definition |
| 2 | Implement `TransportPublisher` in Core | M | Header generation, serialization, transport send |
| 3 | Add batch publishing (`PublishManyAsync`) | M | Efficient bulk operations |
| 4 | Add DiagnosticListener/tracing integration | S | Observability |
| 5 | Register in DI via `IMessagingBuilder` | S | Service registration |
| 6 | Write unit tests | M | Mock transport, verify headers, error handling |
| 7 | Write integration tests | M | Real transport (InMemory, RabbitMQ) |
| 8 | Update README and XML docs | S | Documentation |

## Acceptance Criteria

### Functional Requirements

- [x] [S] `ITransportPublisher` interface defined in `Headless.Messaging.Abstractions`
- [ ] [M] Messages published via `ITransportPublisher` are received by consumers
- [ ] [S] System headers auto-generated (MessageId, CorrelationId, SentTime, Type, MessageName)
- [ ] [S] User-provided headers merged correctly (user values win for non-reserved keys)
- [ ] [S] `TopicMappings` resolved for type-safe `PublishAsync<T>(T value)` overloads
- [ ] [S] `TopicNamePrefix` applied when configured
- [ ] [M] `PublishManyAsync` sends multiple messages efficiently
- [ ] [S] `OperateResult.Failed` returned on transport errors (no throw)
- [ ] [S] Exceptions thrown for programming errors (null topic, missing mapping, serialization failure)

### Non-Functional Requirements

- [ ] [S] No database/storage dependencies
- [ ] [S] Thread-safe for concurrent publishing
- [ ] [XS] Singleton DI lifetime
- [ ] [S] OpenTelemetry span created for each publish

### Quality Gates

- [ ] [M] ≥85% line coverage, ≥80% branch coverage
- [ ] [S] All unit tests passing
- [ ] [M] Integration tests with InMemoryQueue transport
- [ ] [S] XML documentation complete for public APIs
- [ ] [XS] CSharpier formatted

## MVP

### ITransportPublisher.cs (Abstractions)

```csharp
namespace Headless.Messaging;

/// <summary>
/// A publishing service for publishing messages directly to the transport,
/// bypassing the outbox persistence layer. Use for fire-and-forget scenarios
/// where message durability is not required.
/// </summary>
/// <remarks>
/// Unlike <see cref="IOutboxPublisher"/>, messages published via this interface:
/// <list type="bullet">
/// <item><description>Are not persisted to the outbox storage</description></item>
/// <item><description>Are not retried on failure</description></item>
/// <item><description>Cannot participate in database transactions</description></item>
/// </list>
/// Use this for low-priority messages like metrics, telemetry, or notifications
/// where occasional message loss is acceptable.
/// </remarks>
public interface ITransportPublisher
{
    /// <summary>
    /// Asynchronously publishes a message directly to the transport.
    /// </summary>
    /// <typeparam name="T">The type of the message content object.</typeparam>
    /// <param name="name">The topic name or exchange router key.</param>
    /// <param name="value">The message body content that will be serialized. (can be null)</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An <see cref="OperateResult"/> indicating success or failure.</returns>
    Task<OperateResult> PublishAsync<T>(
        string name,
        T? value,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Asynchronously publishes a message directly to the transport with custom headers.
    /// </summary>
    /// <typeparam name="T">The type of the message content object.</typeparam>
    /// <param name="name">The topic name or exchange router key.</param>
    /// <param name="value">The message body content that will be serialized. (can be null)</param>
    /// <param name="headers">Additional headers to include in the message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An <see cref="OperateResult"/> indicating success or failure.</returns>
    Task<OperateResult> PublishAsync<T>(
        string name,
        T? value,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Asynchronously publishes a message using topic mapping configured via WithTopicMapping.
    /// The topic name is inferred from the message type T.
    /// </summary>
    /// <typeparam name="T">The type of the message content object. Must have a registered topic mapping.</typeparam>
    /// <param name="value">The message body content that will be serialized. (can be null)</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An <see cref="OperateResult"/> indicating success or failure.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no topic mapping exists for type T.</exception>
    Task<OperateResult> PublishAsync<T>(T? value, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Asynchronously publishes a message using topic mapping with custom headers.
    /// The topic name is inferred from the message type T.
    /// </summary>
    /// <typeparam name="T">The type of the message content object. Must have a registered topic mapping.</typeparam>
    /// <param name="value">The message body content that will be serialized. (can be null)</param>
    /// <param name="headers">Additional headers to include in the message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An <see cref="OperateResult"/> indicating success or failure.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no topic mapping exists for type T.</exception>
    Task<OperateResult> PublishAsync<T>(
        T? value,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class;

    /// <summary>
    /// Asynchronously publishes multiple messages directly to the transport.
    /// More efficient than calling <see cref="PublishAsync{T}(string, T?, CancellationToken)"/> in a loop.
    /// </summary>
    /// <typeparam name="T">The type of the message content objects.</typeparam>
    /// <param name="name">The topic name or exchange router key.</param>
    /// <param name="values">The message bodies to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of <see cref="OperateResult"/> for each message, in order.</returns>
    Task<IReadOnlyList<OperateResult>> PublishManyAsync<T>(
        string name,
        IEnumerable<T?> values,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Asynchronously publishes multiple messages using topic mapping.
    /// More efficient than calling <see cref="PublishAsync{T}(T?, CancellationToken)"/> in a loop.
    /// </summary>
    /// <typeparam name="T">The type of the message content objects. Must have a registered topic mapping.</typeparam>
    /// <param name="values">The message bodies to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of <see cref="OperateResult"/> for each message, in order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no topic mapping exists for type T.</exception>
    Task<IReadOnlyList<OperateResult>> PublishManyAsync<T>(
        IEnumerable<T?> values,
        CancellationToken cancellationToken = default
    )
        where T : class;
}
```

### TransportPublisher.cs (Core)

```csharp
namespace Headless.Messaging.Internal;

internal sealed class TransportPublisher(IServiceProvider serviceProvider) : ITransportPublisher
{
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly MessagingOptions _options = serviceProvider
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value;
    private readonly ISerializer _serializer = serviceProvider.GetRequiredService<ISerializer>();
    private readonly ITransport _transport = serviceProvider.GetRequiredService<ITransport>();
    private readonly ILongIdGenerator _idGenerator = serviceProvider.GetRequiredService<ILongIdGenerator>();
    private readonly TimeProvider _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

    public Task<OperateResult> PublishAsync<T>(
        string name,
        T? value,
        CancellationToken cancellationToken = default
    )
    {
        return PublishAsync(name, value, new Dictionary<string, string?>(StringComparer.Ordinal), cancellationToken);
    }

    public async Task<OperateResult> PublishAsync<T>(
        string name,
        T? value,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(name);
        Argument.IsNotNull(headers);

        var message = _CreateMessage(name, value, headers);
        var transportMessage = await _serializer.SerializeToTransportMessageAsync(message).AnyContext();

        var tracingTimestamp = _TracingBefore(transportMessage);

        var result = await _transport.SendAsync(transportMessage).AnyContext();

        if (result.Succeeded)
        {
            _TracingAfter(tracingTimestamp, transportMessage);
        }
        else
        {
            _TracingError(tracingTimestamp, transportMessage, result);
        }

        return result;
    }

    public Task<OperateResult> PublishAsync<T>(T? value, CancellationToken cancellationToken = default)
        where T : class
    {
        var topicName = _GetTopicNameFromMapping<T>();
        return PublishAsync(topicName, value, cancellationToken);
    }

    public Task<OperateResult> PublishAsync<T>(
        T? value,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var topicName = _GetTopicNameFromMapping<T>();
        return PublishAsync(topicName, value, headers, cancellationToken);
    }

    public async Task<IReadOnlyList<OperateResult>> PublishManyAsync<T>(
        string name,
        IEnumerable<T?> values,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(name);
        Argument.IsNotNull(values);

        var results = new List<OperateResult>();

        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await PublishAsync(name, value, cancellationToken).AnyContext();
            results.Add(result);
        }

        return results;
    }

    public Task<IReadOnlyList<OperateResult>> PublishManyAsync<T>(
        IEnumerable<T?> values,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var topicName = _GetTopicNameFromMapping<T>();
        return PublishManyAsync(topicName, values, cancellationToken);
    }

    private Message _CreateMessage<T>(string name, T? value, IDictionary<string, string?> headers)
    {
        // Apply topic prefix
        if (!string.IsNullOrEmpty(_options.TopicNamePrefix))
        {
            name = $"{_options.TopicNamePrefix}.{name}";
        }

        // Generate system headers (user values take precedence)
        var messageId = _idGenerator.Create().ToString(CultureInfo.InvariantCulture);

        if (!headers.ContainsKey(Headers.MessageId))
        {
            headers[Headers.MessageId] = messageId;
        }

        if (!headers.ContainsKey(Headers.CorrelationId))
        {
            headers[Headers.CorrelationId] = headers[Headers.MessageId];
            headers[Headers.CorrelationSequence] = "0";
        }

        headers[Headers.MessageName] = name;
        headers[Headers.Type] = typeof(T).Name;
        headers[Headers.SentTime] = _timeProvider.GetUtcNow().UtcDateTime.ToString(CultureInfo.InvariantCulture);

        return new Message(headers, value);
    }

    private string _GetTopicNameFromMapping<T>()
        where T : class
    {
        var messageType = typeof(T);

        if (!_options.TopicMappings.TryGetValue(messageType, out var topicName))
        {
            throw new InvalidOperationException(
                $"No topic mapping found for message type '{messageType.Name}'. "
                    + $"Register a topic mapping using WithTopicMapping<{messageType.Name}>(\"topic-name\") "
                    + "or use the overload that accepts an explicit topic name."
            );
        }

        return topicName;
    }

    // ... tracing methods (same pattern as MessageSender)
}
```

### ServiceCollectionExtensions.cs (Registration)

```csharp
// In Setup.cs or ServiceCollectionExtensions.cs
services.TryAddSingleton<ITransportPublisher, TransportPublisher>();
```

## Dependencies & Risks

### Dependencies

- `ITransport` must be registered (transport-specific package required)
- `ISerializer` must be registered (typically auto-registered by Core)
- `ILongIdGenerator` must be registered (from `Headless.Abstractions`)
- `TimeProvider` must be registered

### Risks

| Risk | Mitigation |
|------|------------|
| Message loss on transport failure | Documented behavior; users choose based on requirements |
| Breaking change if API evolves | Interface-based; can add methods without breaking |
| Performance regression in batch API | Profile; consider transport-specific batch optimizations |

## References & Research

### Internal References

- Existing publisher: `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs`
- Transport interface: `src/Headless.Messaging.Core/Transport/ITransport.cs`
- Message sender pattern: `src/Headless.Messaging.Core/Internal/IMessageSender.cs`
- Header constants: `src/Headless.Messaging.Abstractions/Messages/Headers.cs`

### Design Decisions

1. **Separate interface vs extension methods** - Separate interface chosen for clean DI, explicit intent
2. **Return `OperateResult` vs `Task`** - Explicit result enables error handling without exceptions
3. **Include batch API initially** - User requested for metrics/logs scenarios
4. **Auto-generate headers** - Consistency with outbox path, proper distributed tracing

## Unresolved Questions

1. **Transport-specific batch optimizations** - Some transports (Kafka, SQS) support native batching. Should `PublishManyAsync` leverage this?
   - *Recommendation*: Start with sequential sends; optimize per-transport in v2

2. **Delayed publishing** - Should `PublishDelayAsync` be supported?
   - *Recommendation*: Defer; requires transport-specific support and adds complexity

3. **Callback support** - Should `callbackName` parameter be supported?
   - *Recommendation*: Defer; fire-and-forget pattern typically doesn't need callbacks
