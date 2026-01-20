---
status: pending
priority: p3
issue_id: "068"
tags: [code-review, cleanup, rabbitmq, naming]
created: 2026-01-20
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

- [ ] Replace "cap.default.router" with new name
- [ ] Search for other CAP references
- [ ] Update comments/docs
- [ ] Update README if CAP mentioned
- [ ] Run integration tests

**Effort:** 30 min | **Risk:** Very Low
