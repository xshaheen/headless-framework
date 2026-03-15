---
status: pending
priority: p3
issue_id: "008"
tags: ["code-review","api-design","audit"]
dependencies: []
---

# IAuditLog.LogAsync is a misleadingly async API — implementation is always synchronous

## Problem Statement

IAuditLog.LogAsync returns Task but both the interface contract ('added to the current DbContext, persists with next SaveChanges') and the implementation (EfAuditLog.LogAsync always returns Task.CompletedTask) are entirely synchronous. The async signature misleads consumers into thinking await is meaningful here, and the CancellationToken parameter is ignored. For a public API surface in a framework, a sync method or ValueTask would be more honest. The Task-returning signature also prevents a NullAuditLog or no-op implementation from easily returning a cached Task, though Task.CompletedTask is available.

## Findings

- **IAuditLog.LogAsync signature:** src/Headless.AuditLog.Abstractions/IAuditLog.cs:22-30
- **EfAuditLog.LogAsync implementation:** src/Headless.AuditLog.EntityFramework/EfAuditLog.cs:19-55

## Proposed Solutions

### Keep async Task signature but document why (future implementations may be truly async)
- **Pros**: No API break. Maintains flexibility for non-EF implementations (e.g., write to message bus).
- **Cons**: Misleading for EF implementation specifically.
- **Effort**: Small
- **Risk**: Low

### Change to ValueTask to signal the sync-first nature while allowing async implementations
- **Pros**: Allocates less for sync implementations. Semantically clearer.
- **Cons**: Breaking API change for existing consumers.
- **Effort**: Small
- **Risk**: Medium

### Keep as-is with improved XML doc noting CancellationToken is reserved for future use
- **Pros**: No change needed.
- **Cons**: Misleading API remains.
- **Effort**: Minimal
- **Risk**: Low


## Recommended Action

Keep Task signature (the EF implementation is synchronous but a message-bus implementation could be truly async). Add XML doc on CancellationToken noting it is forwarded to future async implementations. Add a note that the EF implementation returns immediately — the entry persists with the next SaveChanges call. This matches the documented behavior already in the interface, just needs explicit CancellationToken callout.

## Acceptance Criteria

- [ ] IAuditLog.LogAsync XML docs clarify when the CancellationToken is used
- [ ] The sync nature of EfAuditLog.LogAsync is documented

## Notes

Discovered during PR #187 review. This is a design taste issue, not a correctness bug. The async signature keeps the door open for non-EF implementations.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
