---
status: done
priority: p3
issue_id: "004"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Document retry backpressure monitor for agents and operators

## Problem Statement

The PR registers IRetryProcessorMonitor and exposes CurrentPollingInterval, IsBackedOff, and ResetBackpressureAsync, but the agent-facing docs only describe ICircuitBreakerMonitor. That leaves the new backpressure controls discoverable in code but effectively invisible to agents and operators following the published guidance.

## Findings

- **Location:** src/Headless.Messaging.Core/README.md:267-280
- **Location:** docs/llms/messaging.txt:432-438
- **Related code:** src/Headless.Messaging.Core/CircuitBreaker/IRetryProcessorMonitor.cs
- **Discovered by:** compound-engineering:review agent-native-reviewer

## Proposed Solutions

### Add retry monitor examples next to circuit-breaker docs
- **Pros**: Small change, immediately improves discoverability
- **Cons**: Requires keeping two monitor sections in sync
- **Effort**: Small
- **Risk**: Low

### Create one combined programmatic operations section
- **Pros**: Presents both breaker and retry recovery paths together
- **Cons**: Slightly larger doc edit
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Document IRetryProcessorMonitor in README and llms docs with inspect/reset examples alongside ICircuitBreakerMonitor.

## Acceptance Criteria

- [ ] README includes how to inspect CurrentPollingInterval and IsBackedOff
- [ ] README includes how to call ResetBackpressureAsync
- [ ] docs/llms/messaging.txt mirrors the same monitor guidance

## Notes

PR #194 code review finding from the required agent-native review pass.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
