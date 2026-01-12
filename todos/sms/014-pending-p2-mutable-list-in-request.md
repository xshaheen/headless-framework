---
status: pending
priority: p2
issue_id: "014"
tags: [code-review, api-design, sms]
dependencies: []
---

# SendSingleSmsRequest uses mutable List<T> for Destinations

## Problem Statement

`SendSingleSmsRequest.Destinations` is typed as `List<SmsRequestDestination>`, exposing implementation details and allowing callers to modify the list after construction.

## Findings

- **File:** `src/Framework.Sms.Abstractions/Contracts/SendSingleSmsRequest.cs:12`
- **Current code:**
```csharp
public required List<SmsRequestDestination> Destinations { get; init; }
```
- Even with `init`, the list contents can be modified after construction
- Exposes implementation details
- Could cause issues if caller modifies list during sending

## Proposed Solutions

### Option 1: Use IReadOnlyList<T>

**Approach:** Change the property type to `IReadOnlyList<T>`.

```csharp
public required IReadOnlyList<SmsRequestDestination> Destinations { get; init; }
```

**Pros:**
- Callers cannot modify contents
- Clear API contract

**Cons:**
- Minor breaking change (callers passing `List<T>` still works)

**Effort:** 30 minutes

**Risk:** Low (non-breaking for callers)

---

### Option 2: Use ImmutableArray<T>

**Approach:** Use `ImmutableArray<T>` for true immutability.

```csharp
public required ImmutableArray<SmsRequestDestination> Destinations { get; init; }
```

**Pros:**
- Guaranteed immutable
- Value semantics

**Cons:**
- Requires `System.Collections.Immutable`
- Slightly different API for construction

**Effort:** 1 hour

**Risk:** Medium (API change)

## Recommended Action

Implement Option 1 (`IReadOnlyList<T>`) for minimal disruption.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Abstractions/Contracts/SendSingleSmsRequest.cs:12`

## Acceptance Criteria

- [ ] Destinations property is immutable from caller's perspective
- [ ] Existing code continues to work

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified mutable collection in public API
- Proposed IReadOnlyList solution
