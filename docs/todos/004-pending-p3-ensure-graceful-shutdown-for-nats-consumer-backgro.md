---
status: pending
priority: p3
issue_id: "004"
tags: []
dependencies: []
---

# Ensure graceful shutdown for NATS consumer background handlers

## Problem Statement

NatsConsumerClient dispatches background handlers via Task.Run but does not await them during DisposeAsync.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p3

## Proposed Solutions

_To be analyzed during triage or investigation._

## Recommended Action

_To be determined during triage._

## Acceptance Criteria
- [ ] DisposeAsync awaits completion of all in-flight message handlers
- [ ] Integration tests confirm no message loss on shutdown

## Notes

Source: Workflow automation

## Work Log

### 2026-04-16 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create
