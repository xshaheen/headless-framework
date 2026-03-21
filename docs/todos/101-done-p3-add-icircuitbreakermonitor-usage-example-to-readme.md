---
status: done
priority: p3
issue_id: "101"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Add ICircuitBreakerMonitor usage example to README and docs/llms/messaging.txt

## Problem Statement

README.md Circuit Breaker section and docs/llms/messaging.txt describe OTel metrics and logging but do not show how to resolve ICircuitBreakerMonitor from DI, call GetAllStates(), or call ResetAsync(). Agents and operators have no documented example for programmatic circuit control.

## Findings

- **Location:** src/Headless.Messaging.Core/README.md (Circuit Breaker section), docs/llms/messaging.txt (circuit breaker block)
- **Risk:** Discoverability gap — programmatic control API undocumented for consumers
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Add Programmatic Control subsection to both files
- **Pros**: Improves discoverability for agents and operators
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add: `var monitor = app.Services.GetRequiredService<ICircuitBreakerMonitor>(); var states = monitor.GetAllStates(); await monitor.ResetAsync("payments", ct);` with explanation in both README.md and docs/llms/messaging.txt.

## Acceptance Criteria

- [ ] README.md has Programmatic Control example with ICircuitBreakerMonitor
- [ ] docs/llms/messaging.txt has matching example
- [ ] GetAllStates, IsOpen, GetState, ResetAsync all shown

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
