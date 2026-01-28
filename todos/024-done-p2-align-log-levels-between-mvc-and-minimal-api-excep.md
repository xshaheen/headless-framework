---
status: done
priority: p2
issue_id: "024"
tags: []
dependencies: []
---

# Align log levels between MVC and Minimal API exception filters

## Problem Statement

MVC filter logs DbUpdateConcurrencyException at LogLevel.Critical but Minimal API filter logs it at LogLevel.Warning. Inconsistent severity affects alerting. Files: Framework.Api.Mvc/Filters/MvcApiExceptionFilter.cs, Framework.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs

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
- [ ] Choose consistent log level (Warning recommended)
- [ ] Document log level decisions
- [ ] Consider removing EF Core dependency from MVC package (use duck typing like Minimal API)

## Notes

Source: Workflow automation

## Work Log

### 2026-01-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-27 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
