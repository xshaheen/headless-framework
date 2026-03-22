---
status: pending
priority: p3
issue_id: "051"
tags: ["code-review","quality"]
dependencies: []
---

# Add end-to-end integration tests for circuit trip/recovery with InMemory transport

## Problem Statement

Plan conformance review found 2 unchecked quality gates: (1) integration test for InMemory transport circuit trip → pause → half-open probe → close end-to-end flow, and (2) integration test verifying retry processor respects open circuits. These were explicitly listed in the plan's Integration Test Scenarios section. Unit tests cover the state machine in isolation; what is missing is the full pipeline test.

## Findings

- **Missing scenarios:** Circuit trip/recovery end-to-end with InMemory transport; retry processor skipping messages for open-circuit groups
- **Discovered by:** plan-conformance-reviewer

## Proposed Solutions

### Add tests to Headless.Messaging.Core.Tests.Unit using InMemory transport
- **Pros**: No Docker required, fast, covers the full pipeline
- **Cons**: None
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Add integration-style unit tests using InMemory transport + real CircuitBreakerStateManager + real ConsumerRegister to verify: (1) N failures trip the breaker → PauseAsync called → messages queued but not consumed, (2) after open duration → HalfOpen probe → success closes, (3) retry processor skips messages for open-circuit groups.

## Acceptance Criteria

- [ ] Test: N consecutive transient failures trip circuit, consumer paused
- [ ] Test: After open duration, HalfOpen probe succeeds, circuit closes, consumer resumes
- [ ] Test: Retry processor skips re-enqueue for open-circuit group
- [ ] Tests run without Docker (InMemory transport only)

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
