---
status: pending
priority: p1
issue_id: "057"
tags: [code-review, rabbitmq, production-ready, cleanup]
created: 2026-01-20
dependencies: []
---

# Remove Console.WriteLine Debug Code

## Problem

**File:** `src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs:166`

```csharp
catch (Exception e)
{
    Console.WriteLine(e);  // DEBUG CODE IN PRODUCTION
}
```

**Issues:**
- Console.WriteLine in library code pollutes consumer output
- No structured logging
- Exception swallowed after print
- Non-professional debug artifact

## Solution

```csharp
catch (Exception e)
{
    _logger.LogError(e, "Failed to close RabbitMQ channel during return");
    // Don't throw - best effort cleanup
}
```

Inject `ILogger<IConnectionChannelPool>` if not already available.

## Acceptance Criteria

- [ ] Remove Console.WriteLine
- [ ] Add proper ILogger call
- [ ] Verify logger injected in constructor
- [ ] Search codebase for other Console.WriteLine instances
- [ ] Run tests

**Effort:** 15 min | **Risk:** Very Low
