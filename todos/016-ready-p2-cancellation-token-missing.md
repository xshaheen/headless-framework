---
status: ready
priority: p2
issue_id: "016"
tags: [async, cancellation, api-design, shutdown]
dependencies: []
---

# Missing CancellationToken Parameters in Async Methods

## Problem Statement

Many async methods lack CancellationToken parameters, preventing graceful shutdown and timeout scenarios.

## Findings

**Affected Methods** (sample):
- `IMessageDispatcher.DispatchAsync` - no cancellation support
- Connection activation methods in RabbitMQ/Kafka
- Some storage methods

**Impact**:
- Cannot cancel in-flight operations during shutdown
- Timeouts cannot be implemented
- Violates async best practices

## Proposed Solutions

### Option 1: Add CancellationToken Parameters (RECOMMENDED)
**Effort**: 4-6 hours
**Breaking Change**: YES (signature change)

```csharp
// Before:
Task DispatchAsync<TMessage>(ConsumeContext<TMessage> context)

// After:
Task DispatchAsync<TMessage>(ConsumeContext<TMessage> context, CancellationToken cancellationToken = default)
```

### Option 2: ConsumeContext.CancellationToken Property
**Effort**: 2-3 hours
**Breaking Change**: NO

Add cancellation token to context:
```csharp
public record ConsumeContext<TMessage>
{
    public CancellationToken CancellationToken { get; init; }
}
```

## Recommended Action

Implement Option 1 for new APIs, Option 2 for internal compatibility.

## Acceptance Criteria

- [ ] All async methods accept CancellationToken
- [ ] Cancellation flows through call chain
- [ ] Shutdown tests verify graceful cancellation
- [ ] Breaking changes documented

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Strict .NET Reviewer Agent)

**Actions:**
- Audited async methods for cancellation support
- Identified missing parameters
- Proposed dual-track solution

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
