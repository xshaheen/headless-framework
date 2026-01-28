---
status: done
priority: p3
issue_id: "035"
tags: [yagni, api-surface, breaking-change]
dependencies: []
---

# Remove synchronous Publish methods from IOutboxPublisher

## Problem Statement

`IOutboxPublisher` has 8 synchronous methods that wrap async counterparts using `.GetAwaiter().GetResult()`. This is sync-over-async anti-pattern.

**File:** `src/Headless.Messaging.Abstractions/IOutboxPublisher.cs:57-122, 162-235`

```csharp
void Publish<T>(string name, T? contentObj, string? callbackName = null);
void Publish<T>(string name, T? contentObj, IDictionary<string, string?> headers);
// ... 6 more sync methods
```

**Implementation in `OutboxPublisher.cs`:**
```csharp
public void Publish<T>(string name, T? value, string? callbackName = null)
{
    PublishAsync(name, value, callbackName).AnyContext().GetAwaiter().GetResult();
}
```

**Problems:**
1. Sync-over-async blocks thread pool threads
2. Potential deadlocks in synchronization contexts
3. API surface bloat (16 methods total)
4. Modern .NET is async-first

## Findings

- **Severity:** Low (P3) - YAGNI violation
- **Impact:** ~80 lines of unnecessary code, API confusion
- **Breaking Change:** Yes - removing public API

## Proposed Solutions

### Option 1: Remove all sync methods (Recommended)
- **Pros**: Clean API, modern patterns, smaller surface
- **Cons**: Breaking change
- **Effort**: Small
- **Risk**: Medium (breaking)

### Option 2: Mark as obsolete first
- **Pros**: Gradual deprecation path
- **Cons**: Delays cleanup
- **Effort**: Small
- **Risk**: Low

## Recommended Action

For v1.0: Remove sync methods. Document as breaking change from preview.

## Acceptance Criteria

- [ ] Remove 8 sync `Publish*` methods from `IOutboxPublisher`
- [ ] Remove implementations from `OutboxPublisher`
- [ ] Update documentation
- [ ] Add migration guide in release notes

## Notes

Source: Pragmatic .NET Reviewer + Simplicity Reviewer agents

## Work Log

### 2026-01-25 - Created

**By:** Code Review Agent
**Actions:**
- Created from multi-agent code review findings
