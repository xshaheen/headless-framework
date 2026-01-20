---
status: pending
priority: p2
issue_id: "063"
tags: [code-review, testing, rabbitmq, quality]
created: 2026-01-20
dependencies: []
---

# Missing Test Coverage (0%)

## Problem

**Zero tests** for Framework.Messages.RabbitMQ provider:
- No unit tests for connection pooling
- No integration tests for pub/sub
- No tests for error paths
- ~854 LOC completely untested

**Risk:** Critical bugs shipped to production.

## Solution

**Unit Tests** (`Framework.Messages.RabbitMQ.Tests.Unit`):
```csharp
// Connection pool tests
- RentAsync_WhenConnectionFails_DecrementsCount
- Return_WhenChannelClosed_DisposesChannel
- GetConnection_WhenCalled_ReusesExistingConnection

// Consumer tests
- ConnectAsync_WhenQueueNotExists_CreatesQueue
- HandleBasicDeliver_WhenCallbackThrows_NacksMessage

// Transport tests
- PublishAsync_WhenExchangeNotExists_CreatesExchange
```

**Integration Tests** (`Framework.Messages.RabbitMQ.Tests.Integration`):
Use Testcontainers.RabbitMQ:
```csharp
[Collection("RabbitMQ")]
public class RabbitMqIntegrationTests
{
    [Fact]
    public async Task PublishAndConsume_EndToEnd()
    {
        // Arrange: Start container, configure client
        // Act: Publish → Consume
        // Assert: Message received
    }
}
```

## Acceptance Criteria

- [ ] Create unit test project
- [ ] Add Testcontainers.RabbitMQ package
- [ ] Write 20+ unit tests (connection pool, consumer, transport)
- [ ] Write 5+ integration tests (end-to-end flows)
- [ ] Achieve >80% line coverage
- [ ] Achieve >70% branch coverage

**Effort:** 8 hours | **Risk:** Low
