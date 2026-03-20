---
status: pending
priority: p2
issue_id: "001"
tags: ["code-review","quality"]
dependencies: []
---

# Add predicate context to MessageObservationTimeoutException

## Problem Statement

When WaitFor* times out with a predicate, the diagnostic message shows 'message type was observed' but doesn't indicate a predicate was active. Users cannot distinguish 'message never arrived' from 'message arrived but predicate rejected it'.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessageObservationTimeoutException.cs:39-80
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Add HasPredicate property and note in _BuildMessage
- **Pros**: Clear diagnostic, minimal change
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Pass hasActivePredicate from WaitForAsync, include note in exception message when type was observed but predicate returned false.

## Acceptance Criteria

- [ ] Exception message distinguishes 'type never arrived' from 'predicate rejected'
- [ ] HasPredicate or equivalent property exposed

## Notes

Source: Code review

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
