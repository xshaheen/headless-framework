---
status: in-progress
priority: p2
issue_id: "077"
tags: ["code-review","messaging","api-design","breaking-change"]
dependencies: []
---

# Add default interface method bodies to IConsumerClient.PauseAsync/ResumeAsync

## Problem Statement

IConsumerClient.PauseAsync and ResumeAsync are declared as abstract interface methods (no default body). The plan and docs describe them as default interface methods (DIMs) for backward compatibility. Any third-party transport implementation not in this repo will fail to compile after upgrading. The PR description and docs/llms/messaging.txt both claim DIM semantics but the interface does not deliver them.

## Findings

- **Location:** src/Headless.Messaging.Core/Transport/IConsumerClient.cs:62-69
- **Problem:** No default body — abstract methods, not DIMs as documented
- **Impact:** Breaking change for any custom transport implementation
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Add DIM bodies returning ValueTask.CompletedTask
- **Pros**: Backward compatible, matches documented intent
- **Cons**: No-op implementations may silently ignore circuit breaker pause — document that custom transports SHOULD implement these
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add `=> ValueTask.CompletedTask` default bodies to both PauseAsync and ResumeAsync. Add XML doc note that custom transports should override for full circuit breaker benefit.

## Acceptance Criteria

- [ ] PauseAsync has DIM body returning ValueTask.CompletedTask
- [ ] ResumeAsync has DIM body returning ValueTask.CompletedTask
- [ ] XML doc notes that custom implementations should override for circuit breaker to take effect
- [ ] Existing transport tests still pass

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
