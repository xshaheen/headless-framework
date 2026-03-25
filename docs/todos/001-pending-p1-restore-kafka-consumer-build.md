---
status: pending
priority: p1
issue_id: "001"
tags: ["code-review","dotnet","build","quality"]
dependencies: []
---

# Restore Kafka consumer build

## Problem Statement

The Kafka transport no longer compiles because `consumerFactory ?? _BuildConsumer` and the admin-client equivalent use a method group on the right-hand side of `??`. This blocks solution builds, Kafka tests, and packaging.

## Findings

- **Location:** src/Headless.Messaging.Kafka/KafkaConsumerClient.cs:26-29
- **Evidence:** dotnet build headless-framework.slnx -c Release fails with CS0019 on the null-coalescing assignment
- **Discovered by:** code review

## Proposed Solutions

### Wrap method groups explicitly
- **Pros**: Smallest code change
- **Cons**: Keeps the current constructor shape
- **Effort**: Small
- **Risk**: Low

### Assign in constructor body
- **Pros**: Clearer initialization semantics
- **Cons**: Slightly more refactor
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace the method-group RHS with explicitly typed delegates and re-run Kafka tests.

## Acceptance Criteria

- [ ] KafkaConsumerClient compiles without CS0019
- [ ] dotnet build headless-framework.slnx -c Release passes this project
- [ ] Kafka unit tests run successfully

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
