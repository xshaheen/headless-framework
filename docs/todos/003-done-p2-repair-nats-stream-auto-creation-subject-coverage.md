---
status: done
priority: p2
issue_id: "003"
tags: ["code-review","dotnet","quality","nats"]
dependencies: []
---

# Repair NATS stream auto-creation subject coverage

## Problem Statement

The NATS migration now auto-creates streams with only `"{streamName}.>"` as the subject set. That misses valid single-token subjects like `orders`, so fresh greenfield deployments can create streams that never capture those messages.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:63-85
- **Risk:** Runtime publish/consume failures or partial subject coverage for single-token subjects
- **Discovered by:** code review

## Proposed Solutions

### Include exact subject coverage
- **Pros**: Fixes bare-subject regression quickly
- **Cons**: None for greenfield rollout
- **Effort**: Small
- **Risk**: Medium

## Recommended Action

Derive stream subjects from the actual topic set, including bare subjects. Do not add upgrade reconciliation logic while the project is still greenfield with no existing consumers.

## Acceptance Criteria

- [x] Single-token subjects like `orders` are backed by the created stream
- [x] Stream subjects are derived from the requested topic set without requiring migration logic
- [x] Regression tests cover fresh-create subject selection behavior

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-25 - Implemented

**By:** Agent
**Actions:**
- Replaced wildcard-only stream subject creation with topic-derived subject coverage
- Added unit tests for bare, hierarchical, and mixed subject sets
- Scoped the fix to greenfield behavior and intentionally skipped upgrade reconciliation

### 2026-03-25 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
