---
status: pending
priority: p3
issue_id: "022"
tags: [api-design, type-safety, developer-experience, publish]
dependencies: []
---

# String-Based Publishing Lacks Type Safety

## Problem Statement

Publisher API requires manual topic name strings, creating mismatch opportunities with consumer registration and limiting compile-time safety.

## Findings

**Current API** (Stringly-typed):
```csharp
await publisher.PublishAsync("orders.created", orderData);
//                             ^^^^^^^^^^^^^^ - Magic string, no validation
```

**Consumer Registration** (Type-safe):
```csharp
messaging.Consumer<OrderCreatedHandler>()
    .Topic("orders.created") // Must match publish string
    .Build();
```

**Problems**:
- Typos not caught until runtime
- Refactoring risk (rename topic in one place, forget others)
- No IntelliSense for topic names

## Proposed Solutions

### Option 1: Topic Mapping with PublishAsync<T>() (RECOMMENDED)
**Effort**: 3-4 hours

```csharp
// Registration:
messaging.WithTopicMapping<OrderCreated>("orders.created");

// Publishing (infers topic from type):
await publisher.PublishAsync(new OrderCreated { OrderId = 123 });
//                             ^^^^^^^^^^^^^^ - Type-safe, IntelliSense works
```

### Option 2: Static Topic Constants
**Effort**: 1-2 hours
**Limitation**: Still manual synchronization

```csharp
public static class Topics
{
    public const string OrderCreated = "orders.created";
}

await publisher.PublishAsync(Topics.OrderCreated, data);
```

### Option 3: Source Generator
**Effort**: 1-2 weeks
**Benefit**: Compile-time topic validation

Generate topic constants from consumer attributes.

## Recommended Action

Implement Option 1 - aligns with IConsume<T> type-safe pattern.

## Acceptance Criteria

- [ ] PublishAsync<T>() overload added
- [ ] Topic mapping registration available
- [ ] Compile-time error if topic not mapped
- [ ] Documentation shows recommended pattern
- [ ] Migration guide from string-based API

## Notes

**Partial Implementation Exists**: WithTopicMapping<T>() already exists but PublishAsync() doesn't use it yet.

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Pragmatic .NET Reviewer)

**Actions:**
- Identified type-safety gap in publish API
- Proposed generic overload solution
- Noted existing infrastructure (topic mapping)
