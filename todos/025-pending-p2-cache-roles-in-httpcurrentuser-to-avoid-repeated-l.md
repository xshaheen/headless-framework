---
status: pending
priority: p2
issue_id: "025"
tags: []
dependencies: []
---

# Cache roles in HttpCurrentUser to avoid repeated LINQ allocations

## Problem Statement

HttpCurrentUser.Roles creates new ImmutableHashSet via LINQ on every access. Multiple role checks per request create unnecessary allocations. File: Framework.BuildingBlocks/Core/ClaimsPrincipalExtensions.cs lines 117-125

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
- [ ] Use Lazy<ImmutableHashSet<string>> for caching
- [ ] Or cache in HttpContext.Items per request

## Notes

Source: Workflow automation

## Work Log

### 2026-01-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create
