---
status: completed
priority: p2
issue_id: "041"
tags: []
dependencies: []
---

# missing-topic-validation

## Problem Statement

WithTopicMapping accepts invalid topic names (null, whitespace, special chars). Can cause runtime errors in message brokers.

## Findings

- **Status:** Resolved
- **Priority:** p2

## Proposed Solutions

### Option 1: Validation in _WithTopicMapping
- **Pros**: Early validation, prevents invalid topics from being registered
- **Cons**: None
- **Effort**: Small
- **Risk**: Low

## Recommended Action

Add comprehensive topic validation to `_WithTopicMapping` method.

## Acceptance Criteria
- [x] Add topic name validation
- [x] Validate format and length on registration
- [x] Reject invalid characters
- [x] Only valid topic names accepted
- [x] Clear error messages for invalid input

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
- Added `_ValidateTopicName` method to `MessagingOptions.cs`
- Validates max length (255 chars), invalid characters, dot placement, consecutive dots
- Added 8 comprehensive tests in `MessagingBuilderTests.cs`
- Status changed: ready → completed
