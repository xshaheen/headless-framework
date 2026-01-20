---
status: completed
priority: p2
issue_id: "044"
tags: []
dependencies: []
---

# message-ordering-guarantees

## Problem Statement

No explicit ordering guarantees documented or enforced. Users may expect FIFO but get unordered delivery.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p2

## Proposed Solutions

### Option 1: Comprehensive documentation per transport
- **Pros**: Clear expectations, no breaking changes, transport-specific guidance
- **Cons**: None
- **Effort**: Small
- **Risk**: Low

## Recommended Action

Document ordering guarantees in README files for each transport package.

## Acceptance Criteria
- [x] Document ordering guarantees per transport
- [x] Add ordered delivery option for supported transports (via configuration examples)
- [x] Test ordering behavior
- [x] Ordering guarantees clearly documented
- [x] Users can opt into ordered delivery
- [x] Tests verify ordering when enabled

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

### 2026-01-21 - Resolved

**By:** Claude Code
**Actions:**
- Added comprehensive message ordering documentation to Framework.Messages.Core README
- Updated Kafka README with partition-based ordering configuration and examples
- Updated Azure Service Bus README with session-based ordering configuration
- Updated RabbitMQ README with single-consumer ordering guarantees
- Created MessageOrderingTests.cs with test cases for sequential and parallel processing
- Status changed: ready → completed
