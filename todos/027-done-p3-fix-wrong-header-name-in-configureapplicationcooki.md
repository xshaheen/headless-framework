---
status: done
priority: p3
issue_id: "027"
tags: []
dependencies: []
---

# Fix wrong header name in ConfigureApplicationCookiesExtensions

## Problem Statement

Setting HttpHeaderNames.Locale header with RedirectUri value looks incorrect. If intent is redirect location, should use Location header. Appears to be copy-paste error. File: src/Framework.Api/Extensions/Cookies/ConfigureApplicationCookiesExtensions.cs lines 15-28

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p3

## Proposed Solutions

### Option 1: [Primary solution]
- **Pros**: [Benefits]
- **Cons**: [Drawbacks]
- **Effort**: Small/Medium/Large
- **Risk**: Low/Medium/High

## Recommended Action

[To be filled during triage]

## Acceptance Criteria
- [ ] Review and fix header name or document intentional behavior

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
