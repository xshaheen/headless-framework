---
status: ready
priority: p2
issue_id: "005"
tags: ["code-review","architecture","dotnet","quality"]
dependencies: []
---

# Document or fix JsonElement round-trip behavior in AuditLogEntry OldValues/NewValues

## Problem Statement

AuditLogEntryConfiguration.cs uses `JsonSerializer.Deserialize<Dictionary<string, object?>>` for OldValues, NewValues, ChangedFields. System.Text.Json deserializes object? values as JsonElement, not original CLR types. A decimal stored as 99.99 comes back as JsonElement(Number). An int stored as 42 comes back as JsonElement. Consumers who access `entry.OldValues["Amount"]` expecting decimal get an InvalidCastException. There is no current test covering numeric/boolean value round-trips. This is especially misleading because the values written (from EF property values) are typed correctly.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs:39-53
- **Severity:** Correctness trap for consumers doing programmatic value comparison
- **Discovered by:** strict-dotnet-reviewer, security-sentinel, performance-oracle

## Proposed Solutions

### Option A: Add XML docs warning about JsonElement read-back
- **Pros**: Minimal change, unblocks this PR
- **Cons**: Consumers still face the JsonElement friction
- **Effort**: Trivial
- **Risk**: Low

### Option B: Change stored type to Dictionary<string, JsonElement>
- **Pros**: Contract is explicit at compile time
- **Cons**: Breaking change for callers passing Dictionary<string, object?>
- **Effort**: Small
- **Risk**: Medium

### Option C: Use typed audit value DTO instead of object?
- **Pros**: Fully type-safe, survives round-trips cleanly
- **Cons**: Larger refactor
- **Effort**: Large
- **Risk**: Medium


## Recommended Action

Short-term: Option A — add XML docs on OldValues, NewValues that values are deserialized as JsonElement for non-string types, and add a test verifying numeric round-trip behavior. Long-term: evaluate Option B.

## Acceptance Criteria

- [ ] XML docs on AuditLogEntry.OldValues and NewValues state that values are JsonElement on read
- [ ] Integration test added for numeric and boolean value round-trip
- [ ] Test documents that JsonElement.GetDecimal() is needed, not direct cast

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
