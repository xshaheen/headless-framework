---
status: done
priority: p2
issue_id: "011"
tags: []
dependencies: []
---

# Fix or remove broken WithConcurrency configuration

## Problem Statement

ConsumerBuilder.WithConcurrency() sets ConsumerMetadata.Concurrency but value never propagates to MethodMatcherCache.GroupConcurrent which always defaults to 1.

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
- [x] Either propagate value through to consumer clients OR remove the broken API to avoid confusion

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

### 2026-01-25 - Fixed

**By:** Agent
**Actions:**
- Added `Concurrency` property to `ConsumerExecutorDescriptor`
- Updated `ConsumerServiceSelector` to propagate `Concurrency` from `ConsumerMetadata`
- Updated `MethodMatcherCache` to use maximum concurrency per group instead of hardcoded 1
- Added unit tests for concurrency propagation
- Status changed: ready → done
