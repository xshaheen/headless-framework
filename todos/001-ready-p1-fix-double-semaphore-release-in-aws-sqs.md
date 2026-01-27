---
status: ready
priority: p1
issue_id: "001"
tags: []
dependencies: []
---

# Fix double semaphore release in AWS SQS

## Problem Statement

AmazonSqsConsumerClient.cs:80-116 releases semaphore in catch block, then calls RejectAsync which releases again. This corrupts semaphore count.

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
- [ ] Remove _semaphore.Release() from catch block since RejectAsync already handles release in finally

## Notes

Source: Workflow automation

## Work Log

### 2026-01-24 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-24 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
