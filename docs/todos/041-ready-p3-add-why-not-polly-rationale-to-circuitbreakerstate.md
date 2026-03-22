---
status: ready
priority: p3
issue_id: "041"
tags: ["code-review","documentation"]
dependencies: []
---

# Add 'why not Polly' rationale to CircuitBreakerStateManager class doc

## Problem Statement

The 858-line CircuitBreakerStateManager has no explanation of why Polly's built-in circuit breaker was not used. Every maintainer will ask. The rationale (Polly operates per-call, not transport-level pause/resume) was in the deleted brainstorm doc.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:1
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add one-sentence XML doc: 'Polly CircuitBreakerStrategyOptions operates at the per-call pipeline level and cannot coordinate transport-level pause/resume across a consumer group.'

## Acceptance Criteria

- [ ] Class XML doc explains why Polly was not used

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
