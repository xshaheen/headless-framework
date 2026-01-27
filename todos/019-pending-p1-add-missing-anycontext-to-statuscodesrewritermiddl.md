---
status: pending
priority: p1
issue_id: "019"
tags: []
dependencies: []
---

# Add missing AnyContext() to StatusCodesRewriterMiddleware

## Problem Statement

StatusCodesRewriterMiddleware.cs is missing AnyContext() (ConfigureAwait(false) equivalent) on async calls, inconsistent with other middlewares. This can cause unnecessary synchronization context capture in library code. File: src/Framework.Api/Middlewares/StatusCodesRewriterMiddleware.cs

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
- [ ] Add .AnyContext() to await next(context)
- [ ] Add .AnyContext() to await Results.Problem().ExecuteAsync()

## Notes

Source: Workflow automation

## Work Log

### 2026-01-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create
