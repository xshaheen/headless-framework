---
status: pending
priority: p2
issue_id: "003"
tags: ["code-review","dotnet","quality","nats"]
dependencies: []
---

# Repair NATS stream auto-creation subject coverage

## Problem Statement

The NATS migration now auto-creates streams with only `"{streamName}.>"` as the subject set and treats 409-conflict as success. That misses valid single-token subjects like `orders`, and it does not converge pre-existing explicit-subject streams to the new wildcard scheme, so upgrades can silently stop capturing new subjects.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:63-85
- **Risk:** Runtime publish/consume failures or partial subject coverage after upgrade
- **Discovered by:** code review

## Proposed Solutions

### Include exact subject coverage
- **Pros**: Fixes bare-subject regression quickly
- **Cons**: Still needs upgrade handling for existing streams
- **Effort**: Small
- **Risk**: Medium

### Merge/update stream subjects during startup
- **Pros**: Handles both new and existing streams safely
- **Cons**: More JetStream management logic
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Derive stream subjects from the actual topic set, including bare subjects, and explicitly reconcile existing stream configs instead of ignoring 409 conflicts.

## Acceptance Criteria

- [ ] Single-token subjects like `orders` are backed by the created stream
- [ ] Existing streams created by older versions are reconciled or validated on startup
- [ ] Regression tests cover fresh-create and upgrade paths

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
