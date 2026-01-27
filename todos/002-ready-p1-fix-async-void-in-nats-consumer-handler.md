---
status: ready
priority: p1
issue_id: "002"
tags: []
dependencies: []
---

# Fix async void in NATS consumer handler

## Problem Statement

NATSConsumerClient.cs:148 uses async void which crashes process on unhandled exception. Warning VSTHRD100 suppressed but still dangerous.

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
- [ ] Wrap handler in try-catch covering all code paths OR use channel-based buffering instead of event handler

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
