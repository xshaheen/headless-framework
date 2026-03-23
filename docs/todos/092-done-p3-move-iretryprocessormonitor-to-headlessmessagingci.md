---
status: done
priority: p3
issue_id: "092"
tags: ["code-review","messaging","api-design"]
dependencies: []
---

# Move IRetryProcessorMonitor to Headless.Messaging.CircuitBreaker namespace for consistency

## Problem Statement

IRetryProcessorMonitor is in namespace Headless.Messaging while ICircuitBreakerMonitor and all related CB types are in Headless.Messaging.CircuitBreaker. A developer or agent injecting both monitors needs two different using directives. The retry backpressure is conceptually part of the circuit breaker system.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/IRetryProcessorMonitor.cs:3
- **Problem:** Namespace Headless.Messaging vs Headless.Messaging.CircuitBreaker for related interfaces
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Move IRetryProcessorMonitor to Headless.Messaging.CircuitBreaker namespace
- **Pros**: Consistent namespace for all CB-related public API
- **Cons**: Breaking namespace change for any existing consumers
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Move IRetryProcessorMonitor to namespace Headless.Messaging.CircuitBreaker. Update all call sites and the README namespace import examples.

## Acceptance Criteria

- [ ] IRetryProcessorMonitor namespace updated
- [ ] All using directives updated
- [ ] README example imports updated

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

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
