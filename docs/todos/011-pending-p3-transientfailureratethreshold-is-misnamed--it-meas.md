---
status: pending
priority: p3
issue_id: "011"
tags: ["code-review","messaging","retry","dotnet","quality"]
dependencies: []
---

# _transientFailureRateThreshold is misnamed — it measures circuit-open rate not transient failure rate

## Problem Statement

_AdjustPollingInterval computes transientRate = skippedCircuitOpen / total. This measures the fraction of retry-eligible messages blocked by open circuits, NOT the transient failure rate of the system. The field name _transientFailureRateThreshold and log/XML doc text saying 'transient rate' are misleading.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:_AdjustPollingInterval
- **Discovered by:** architecture-strategist, code-simplicity-reviewer

## Proposed Solutions

### Rename to _circuitOpenRateThreshold and update XML docs and log messages
- **Pros**: Accurate naming
- **Cons**: None
- **Effort**: Tiny
- **Risk**: Low


## Recommended Action

Rename field and config property to CircuitOpenRateThreshold. Update all XML docs and log message text.

## Acceptance Criteria

- [ ] Field and option accurately named
- [ ] Log messages say 'circuit-open rate' not 'transient failure rate'

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
