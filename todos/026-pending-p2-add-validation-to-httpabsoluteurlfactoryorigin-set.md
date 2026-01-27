---
status: pending
priority: p2
issue_id: "026"
tags: []
dependencies: []
---

# Add validation to HttpAbsoluteUrlFactory.Origin setter

## Problem Statement

Origin setter splits URL but doesn't validate array bounds. If input doesn't contain '://', accessing split[^1] may crash or behave unexpectedly. Also modifying HttpContext.Request.Scheme/Host could cause race conditions. File: src/Framework.Api/Abstractions/HttpAbsoluteUrlFactory.cs lines 30-58

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
- [ ] Add validation: if (split.Length < 2) throw ArgumentException
- [ ] Document thread-safety requirements

## Notes

Source: Workflow automation

## Work Log

### 2026-01-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create
