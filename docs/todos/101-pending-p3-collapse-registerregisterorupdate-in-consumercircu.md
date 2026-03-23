---
status: pending
priority: p3
issue_id: "101"
tags: ["code-review","dotnet","simplicity"]
dependencies: []
---

# Collapse Register/RegisterOrUpdate in ConsumerCircuitBreakerRegistry

## Problem Statement

Register throws on duplicate, RegisterOrUpdate silently overwrites. The throwing variant is only used at startup where duplicate detection is a developer error caught by tests, not a runtime guard. Also Remove() is YAGNI — only used for stale-key cleanup during builder reconfiguration at startup.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs:33-64
- **Discovered by:** compound-engineering:review:code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Use RegisterOrUpdate everywhere. Remove Register and Remove methods. ~15 LOC reduction.

## Acceptance Criteria

- [ ] Single registration method in ConsumerCircuitBreakerRegistry

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
