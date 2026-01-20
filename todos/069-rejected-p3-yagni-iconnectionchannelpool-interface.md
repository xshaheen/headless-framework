---
status: rejected
priority: p3
issue_id: "069"
tags: [code-review, yagni, simplification, rabbitmq]
created: 2026-01-20
resolved: 2026-01-21
resolution: wontfix
dependencies: []
---

# YAGNI: IConnectionChannelPool Interface

## Problem

**File:** `src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs:13-24`

```csharp
public interface IConnectionChannelPool
{
    string HostAddress { get; }
    string Exchange { get; }
    IConnection GetConnection();
    Task<IChannel> Rent();
    bool Return(IChannel context);
}
```

**Original claim:** No evidence interface needed (0 tests, no DI)

## Investigation Findings

**Interface IS used for test isolation:**
- `RabbitMqTransportTests.cs:23` - mocks `IConnectionChannelPool` to test transport in isolation
- `RabbitMqConsumerClientTests.cs:26` - mocks `IConnectionChannelPool` to test consumer in isolation
- Registered in DI: `Setup.cs:46` - `services.AddSingleton<IConnectionChannelPool, ConnectionChannelPool>()`
- Used via constructor injection in 3 classes:
  - `RabbitMqTransport` (line 18)
  - `RabbitMQConsumerClient` (line 15)
  - `RabbitMQConsumerClientFactory` (line 11)

## Resolution

**REJECTED - Interface serves valid purpose:**

1. **Test isolation** - Allows unit testing of transport/consumer without real RabbitMQ connection pooling
2. **Proper DI pattern** - Registered and injected through DI container
3. **Follows framework conventions** - Abstraction allows testing external dependencies

**Conclusion:** Original analysis was incorrect. Interface follows established testing patterns.

**Effort:** Investigation: 15 min | **Risk:** N/A
