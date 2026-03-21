---
status: pending
priority: p3
issue_id: "116"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Confirm KafkaConsumerClient public visibility is intentional

## Problem Statement

KafkaConsumerClient is `public sealed class` while every other transport client in this PR (NatsConsumerClient, AmazonSqsConsumerClient, AzureServiceBusConsumerClient, InMemoryConsumerClient, PulsarConsumerClient, RedisConsumerClient) is `internal sealed class` or `internal class`. Public visibility without XML docs on the class and constructor parameters adds it to the public API surface of Headless.Messaging.Kafka without a documented stability guarantee.

## Findings

- **Location:** src/Headless.Messaging.Kafka/KafkaConsumerClient.cs:14
- **Risk:** Unintended public API surface addition without XML documentation
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Change to internal sealed class if not intentionally public
- **Pros**: Consistent with all other transport clients
- **Cons**: Breaking change if already consumed externally
- **Effort**: Small
- **Risk**: Low

### Add XML doc and confirm intent if public is required
- **Pros**: Documents the API surface
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Confirm whether public visibility is intentional. If not, change to internal. If intentional, add XML documentation to the class.

## Acceptance Criteria

- [ ] KafkaConsumerClient visibility confirmed as intentional or changed to internal
- [ ] XML doc added if public

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
