---
status: ready
priority: p2
issue_id: "113"
tags: ["code-review","architecture","api-design"]
dependencies: []
---

# ICircuitBreakerMonitor naming misleading — exposes mutations on a 'monitor' interface

## Problem Statement

ICircuitBreakerMonitor XML doc says 'read-only view' but exposes ResetAsync and ForceOpenAsync mutations. The ICircuitBreakerStateManager/ICircuitBreakerMonitor split gives a false impression of read-only vs read-write when both have mutations. The two-interface hierarchy adds ~68 LOC with no real access protection.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs, ICircuitBreakerStateManager.cs
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer, compound-engineering:review:code-simplicity-reviewer, compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Fix the XML docs to not say 'read-only'. Consider collapsing the two-interface hierarchy — keep ICircuitBreakerMonitor as public interface, make internal-only pipeline methods internal on the concrete class.

## Acceptance Criteria

- [ ] XML docs accurately describe the interface (not 'read-only')
- [ ] Consider: collapse ICircuitBreakerStateManager into concrete class internals

## Notes

Three reviewers independently flagged this. The public/internal API split intent is correct, just the naming and docs are misleading.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
