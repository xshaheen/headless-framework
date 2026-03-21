---
status: done
priority: p1
issue_id: "106"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix SNS double-check-lock checks _sqsClient instead of _snsClient

## Problem Statement

In AmazonSqsConsumerClient._ConnectAsync, the inner lock guard for SNS initialization reads `if (_sqsClient == null)` instead of `if (_snsClient == null)`. Under concurrent _ConnectAsync calls, two threads can both pass the outer `_snsClient == null` check and both pass the inner `_sqsClient == null` check, causing two IAmazonSimpleNotificationService instances to be created; the first one is leaked and never disposed.

## Findings

- **Location:** src/Headless.Messaging.AwsSqs/AmazonSqsConsumerClient.cs:229
- **Risk:** SNS client leak under concurrent startup — copy-paste defect in double-check-lock pattern
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Fix inner check to _snsClient == null
- **Pros**: One-line fix, correct double-checked locking
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change `if (_sqsClient == null)` to `if (_snsClient == null)` at line 229.

## Acceptance Criteria

- [ ] Inner lock guard reads _snsClient == null
- [ ] No SNS client leak under concurrent _ConnectAsync calls

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
