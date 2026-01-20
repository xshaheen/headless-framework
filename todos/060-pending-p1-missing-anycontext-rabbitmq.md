---
status: pending
priority: p1
issue_id: "060"
tags: [code-review, dotnet, rabbitmq, framework-convention]
created: 2026-01-20
dependencies: []
---

# Missing AnyContext() Framework Convention

## Problem

Multiple files use `ConfigureAwait(false)` instead of framework's `AnyContext()` extension:

**Violations:**
- `RabbitMQBasicConsumer.cs:44` - 1 violation
- `RabbitMQBasicConsumer.cs:49` - 1 violation
- `RabbitMQConsumerClient.cs:136` - 1 violation
- `RabbitMQTransport.cs:77` - 1 violation

**Why it matters:**
- Framework convention for consistency
- `AnyContext()` is more explicit about intent
- Easier to grep/audit

## Solution

Replace all instances:
```csharp
// Before:
await operation.ConfigureAwait(false);

// After:
await operation.AnyContext();
```

## Acceptance Criteria

- [ ] Replace ConfigureAwait(false) → AnyContext() in RabbitMQBasicConsumer.cs
- [ ] Replace ConfigureAwait(false) → AnyContext() in RabbitMQConsumerClient.cs
- [ ] Replace ConfigureAwait(false) → AnyContext() in RabbitMQTransport.cs
- [ ] Verify builds and tests pass
- [ ] Search for remaining ConfigureAwait in RabbitMQ project

**Effort:** 30 min | **Risk:** Very Low
