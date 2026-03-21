---
status: pending
priority: p3
issue_id: "072"
tags: ["code-review","quality"]
dependencies: []
---

# Missing stress test for concurrent TryAcquireHalfOpenProbe

## Problem Statement

should_allow_only_one_halfopen_probe_at_a_time tests two sequential calls. Real invariant is concurrent calls from multiple consumer threads. Without N-parallel stress test, lock regression won't be caught until production.

## Findings

- **Location:** CircuitBreakerStateManagerTests.cs:163-184
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add stress test: trip circuit, wait for HalfOpen, launch N parallel TryAcquireHalfOpenProbe tasks, assert exactly one returns true.

## Acceptance Criteria

- [ ] Concurrent probe acquisition test exists with N>=10 parallel tasks

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
