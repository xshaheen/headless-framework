---
status: pending
priority: p3
issue_id: "066"
tags: ["code-review","quality"]
dependencies: []
---

# Adaptive polling dual-counter complexity — simplify _AdjustPollingInterval

## Problem Statement

_consecutiveHealthyCycles and _consecutiveCleanCycles have overlapping update rules with interleaved semantics. 7-line comment needed to explain 10 lines of code. The interaction between total==0, below-half-threshold, and between-thresholds paths is hard to reason about.

## Findings

- **Location:** IProcessor.NeedRetry.cs:242-313
- **Discovered by:** pragmatic-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Simplify to single counter with explicit states (Backpressure/Recovering/Healthy), or use exponential moving average of circuit-open rate.

## Acceptance Criteria

- [ ] Polling interval logic uses at most one counter or explicit state enum

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
