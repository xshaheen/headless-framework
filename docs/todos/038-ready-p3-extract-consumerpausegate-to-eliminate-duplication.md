---
status: ready
priority: p3
issue_id: "038"
tags: ["code-review","duplication"]
dependencies: []
---

# Extract ConsumerPauseGate to eliminate duplication across 7 transports

## Problem Statement

_pauseGate, _paused, _CreateCompletedGate(), PauseAsync, ResumeAsync logic is structurally identical across InMemory, RabbitMQ, Kafka, ASB, SQS, NATS, Pulsar, Redis transports. Each reimplements the same ~15 lines. Next transport addition will copy-paste again.

## Findings

- **Location:** All 7+ transport PauseAsync/ResumeAsync implementations
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Create ConsumerPauseGate class with Pause(), Resume(), WaitIfPausedAsync(), Release(). All transports compose it. Also fixes the P1 race condition in one place.

## Acceptance Criteria

- [ ] Single ConsumerPauseGate implementation
- [ ] All transports compose rather than copy-paste

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
