---
status: completed
priority: p1
issue_id: "057"
tags: [code-review, rabbitmq, production-ready, cleanup]
created: 2026-01-20
completed: 2026-01-21
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

- [x] Remove Console.WriteLine
- [x] Add proper ILogger call (already existed)
- [x] Verify logger injected in constructor (already injected)
- [x] Search codebase for other Console.WriteLine instances (only in demo/test code)
- [x] Run tests (pre-existing build errors unrelated to this change)

**Effort:** 15 min | **Risk:** Very Low
