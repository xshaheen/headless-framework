---
status: pending
priority: p3
issue_id: "014"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Use JSON array instead of comma-join for composite EntityId in audit records

## Problem Statement

EfAuditChangeCapture._GetEntityIdentity joins composite PK values with ',' (lines 394, 408). If a key component contains a comma (e.g., string PK 'foo,bar'), the resulting EntityId 'foo,bar,baz' is ambiguous — could be ['foo', 'bar,baz'] or ['foo,bar', 'baz']. Any system parsing EntityId to reconstruct entity references (audit search, GDPR deletion tooling, compliance reports) will misinterpret composite keys with commas in values.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:394,408
- **Discovered by:** security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Use JSON array encoding for composite keys: `string.Join(",", values)` → `JsonSerializer.Serialize(values.Select(v => v?.ToString()).ToArray())`. For single-value PKs, keep the current plain string (no change). Document that this is a breaking change for any existing stored composite-key EntityId values.

## Acceptance Criteria

- [ ] Composite PK EntityId serialized as JSON array e.g. '["part1","part2"]'
- [ ] Single-PK EntityId unchanged (plain string)
- [ ] Test: composite PK with comma in a key value round-trips correctly
- [ ] Migration note if breaking change affects existing data

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
