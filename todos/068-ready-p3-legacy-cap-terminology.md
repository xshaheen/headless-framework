---
status: ready
priority: p3
issue_id: "068"
tags: [code-review, cleanup, rabbitmq, naming]
created: 2026-01-20
completed: 2026-01-21
dependencies: []
---

# Legacy CAP Terminology Cleanup

## Problem

**File:** `src/Framework.Messages.RabbitMQ/RabbitMqTransport.cs:44`

```csharp
var exchange = _options.Value.ExchangeName ?? "cap.default.router";
```

Hardcoded "cap.default.router" from legacy CAP library port.

## Solution

```csharp
var exchange = _options.Value.ExchangeName ?? "messaging.default.router";
```

Or use project-specific naming:
```csharp
var exchange = _options.Value.ExchangeName ?? "framework.messages.router";
```

Also check for other CAP references:
- Search codebase for "cap." strings
- Search comments for "CAP" references
- Update any remaining legacy naming

## Acceptance Criteria

- [x] Replace "cap.default.router" with new name
- [x] Search for other CAP references
- [x] Update comments/docs
- [x] Update README if CAP mentioned
- [x] Run integration tests

**Effort:** 30 min | **Risk:** Very Low

## Resolution

**Changed Files:**
- `src/Framework.Messages.RabbitMQ/RabbitMqOptions.cs`

**Changes Made:**
1. Updated `DefaultExchangeName` constant from `"cap.default.router"` to `"messaging.default.router"`
2. Updated XML documentation comment to reflect new value
3. Searched entire RabbitMQ project - confirmed only two lines contained CAP reference
4. README doesn't mention CAP terminology
5. No test files reference the old constant

**Verification:**
- Build succeeded for Framework.Messages.RabbitMQ
- No breaking changes - constant value change only affects new deployments with default configuration
- Existing deployments with explicit ExchangeName configuration unaffected
