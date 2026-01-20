---
status: ready
priority: p3
issue_id: "067"
tags: [code-review, code-quality, naming, rabbitmq]
created: 2026-01-20
resolved: 2026-01-21
dependencies: []
---

# Naming Inconsistency: Mq vs MQ vs RabbitMQ

## Problem

Mixed casing throughout codebase:
- `RabbitMqOptions` (Mq)
- `RabbitMqTransport` (Mq)
- `RabbitMQConsumerClient` (MQ)
- `RabbitMQBasicConsumer` (MQ)
- Method: `UseRabbitMq()` (Mq)

**Standard:** "RabbitMQ" is official branding (capital M, capital Q).

## Solution

Rename all to consistent `RabbitMQ`:
- `RabbitMqOptions` → `RabbitMQOptions`
- `RabbitMqTransport` → `RabbitMQTransport`
- `UseRabbitMq()` → `UseRabbitMQ()`

Keep internal classes as-is (less breaking change).

## Acceptance Criteria

- [x] Rename public types to RabbitMQ
- [x] Update extension method names
- [x] Update README references
- [x] Verify no breaking changes in internal APIs
- [x] Run tests

**Effort:** 1 hour | **Risk:** Low (breaking change)
