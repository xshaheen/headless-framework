---
status: ready
priority: p1
issue_id: "032"
tags: []
dependencies: []
---

# consumer-registry-race

## Problem Statement

ConsumerRegistry.Register() has race between _frozen null check and _consumers assignment. Thread A can pass null check while Thread B freezes, causing NullReferenceException.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p1

## Proposed Solutions

### Option 1: [Primary solution]
- **Pros**: [Benefits]
- **Cons**: [Drawbacks]
- **Effort**: Small/Medium/Large
- **Risk**: Low/Medium/High

## Recommended Action

[To be filled during triage]

## Acceptance Criteria
- [ ] Add lock around freeze check and registration
- [ ] Use lock or Interlocked for atomic freeze+check
- [ ] Add concurrent registration stress tests
- [ ] No NullReferenceException under load
- [ ] Thread-safe freeze behavior verified

## Notes

Source: Workflow automation

## Work Log

### 2026-01-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
