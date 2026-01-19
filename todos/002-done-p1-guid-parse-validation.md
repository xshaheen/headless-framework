---
status: done
priority: p1
issue_id: "002"
tags: [code-review, security, messages, critical, input-validation]
created: 2026-01-19
dependencies: []
---

# MessageId and CorrelationId Should Be Strings

## Problem Statement

MessageId and CorrelationId are typed as `Guid` and `Guid?` but parsed from strings at runtime, causing crashes on malformed input and forcing unnecessary GUID constraints.

**Why Critical:** Wrong type choice causes DoS vulnerabilities, limits ID flexibility (snowflake IDs, ULIDs, etc.), and adds parsing overhead.

## Evidence from Reviews

**Security Sentinel (Agent a0fbd6f):**
```csharp
// Line 135, 144
var messageId = Guid.Parse(mediumMessage.Origin.GetId());  // ❌ No validation
correlationId = Guid.Parse(correlationIdStr);  // ❌ Throws FormatException
```

**Attack Vector:**
1. Send message with malformed GUID string
2. `FormatException` crashes processing pipeline
3. Retry picks same message → infinite crash loop

## Proposed Solutions

### Option 1: Change to String Type (Recommended)
**Effort:** Medium
**Risk:** Low - breaking change but cleaner API

```csharp
// ConsumeContext.cs
public sealed class ConsumeContext<TMessage>
{
    public required string MessageId { get; init; }  // Was: Guid
    public required string? CorrelationId { get; init; }  // Was: Guid?
    // Remove Guid.Empty validation, add string.IsNullOrWhiteSpace check
}

// ISubscribeInvoker.Default.cs
var messageId = mediumMessage.Origin.GetId();  // No parsing needed
var correlationId = mediumMessage.Origin.Headers.TryGetValue(Headers.CorrelationId, out var corrId)
    ? corrId
    : null;
```

**Benefits:**
- No parsing crashes - any string is valid
- Supports GUID, snowflake IDs, ULIDs, custom formats
- Simpler code, better performance
- Matches underlying storage (strings in headers)

### Option 2: Add Validation (Quick Fix)
**Effort:** Small
**Risk:** Medium - keeps problematic GUID constraint

Use `Guid.TryParse` and throw on invalid input (band-aid solution).

## Technical Details

**Affected Files:**
- `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs:135,144`

**Impact:**
- Every message with invalid GUID crashes processor
- Message stuck in retry loop
- All consumers blocked

## Acceptance Criteria

- [ ] Change `ConsumeContext<T>.MessageId` from `Guid` to `string`
- [ ] Change `ConsumeContext<T>.CorrelationId` from `Guid?` to `string?`
- [ ] Remove `Guid.Empty` validation in init setters
- [ ] Add `string.IsNullOrWhiteSpace` validation for MessageId
- [ ] Update `ISubscribeInvoker` to pass strings directly (no parsing)
- [ ] Update all tests to use string IDs
- [ ] Update demos if they construct ConsumeContext
- [ ] Document breaking change in CHANGELOG/migration guide

## Work Log

- **2026-01-19:** Issue identified during security review

## Resources

- Security Review: Agent a0fbd6f
- File: `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs`
- Lines: 135, 144

### 2026-01-19 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-19 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
