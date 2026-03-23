---
status: pending
priority: p2
issue_id: "075"
tags: ["code-review","messaging","correctness"]
dependencies: []
---

# Fix dead _disposed field in AmazonSqsConsumerClient and KafkaConsumerClient

## Problem Statement

AmazonSqsConsumerClient and KafkaConsumerClient declare `private int _disposed` and set it via Interlocked.Exchange in DisposeAsync, but PauseAsync/ResumeAsync never check it. The field is set but never read, making the disposal guard dead code. These clients rely on ConsumerPauseGate._disposed instead, but the pattern is inconsistent and the dead field is confusing.

## Findings

- **Location:** src/Headless.Messaging.AwsSqs/AmazonSqsConsumerClient.cs, src/Headless.Messaging.Kafka/KafkaConsumerClient.cs
- **Problem:** _disposed field set but never read in PauseAsync/ResumeAsync guards
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Use the _disposed field as an actual guard in PauseAsync/ResumeAsync
- **Pros**: Consistent with defensive disposal pattern
- **Cons**: Minor additional code
- **Effort**: Small
- **Risk**: Low

### Remove the unused _disposed field and rely on ConsumerPauseGate
- **Pros**: Removes dead code
- **Cons**: Less defensive for edge cases
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Either add guard checks using _disposed in PauseAsync/ResumeAsync, or remove the dead field. Also standardize the _disposed pattern (int+Interlocked vs bool+lock) across all transport clients.

## Acceptance Criteria

- [ ] _disposed field either used as guard or removed
- [ ] Consistent disposal pattern across all 8 transport clients
- [ ] No dead code in disposal path

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
