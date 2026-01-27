---
status: done
priority: p2
issue_id: "008"
tags: []
dependencies: []
---

# Replace static locks with instance-level locks

## Problem Statement

AWS SQS, Kafka, NATS consumer clients use static Lock causing contention across all instances during concurrent initialization.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p2

## Proposed Solutions

### Option 1: [Primary solution]
- **Pros**: [Benefits]
- **Cons**: [Drawbacks]
- **Effort**: Small/Medium/Large
- **Risk**: Low/Medium/High

## Recommended Action

[To be filled during triage]

## Acceptance Criteria
- [x] Use instance-level Lock or Lazy<T> initialization pattern

## Notes

Source: Workflow automation

## Work Log

### 2026-01-24 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-25 - Completed

**By:** Agent
**Actions:**
- Replaced static locks with instance-level locks in 4 files:
  - `src/Headless.Messaging.AwsSqs/AmazonSqsConsumerClient.cs`
  - `src/Headless.Messaging.Kafka/KafkaConsumerClient.cs`
  - `src/Headless.Messaging.Nats/NATSConsumerClient.cs`
  - `src/Headless.Messaging.InMemoryQueue/MemoryQueue.cs`
- Status changed: ready → done
