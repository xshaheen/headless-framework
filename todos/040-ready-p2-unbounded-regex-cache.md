---
status: ready
priority: p2
issue_id: "040"
tags: []
dependencies: []
---

# unbounded-regex-cache

## Problem Statement

MessagingConventions uses compiled regex without cache size limit. Can cause memory growth in multi-tenant scenarios with dynamic topic names.

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
- [ ] Add LRU cache with max size
- [ ] Use ConcurrentDictionary with eviction
- [ ] Limit cache to 1000 entries
- [ ] Memory usage bounded under load
- [ ] Cache evicts least-used entries

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
