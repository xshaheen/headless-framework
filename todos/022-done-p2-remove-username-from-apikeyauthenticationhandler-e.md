---
status: done
priority: p2
issue_id: "022"
tags: []
dependencies: []
---

# Remove username from ApiKeyAuthenticationHandler error messages

## Problem Statement

ApiKeyAuthenticationHandler leaks username in error messages which enables user enumeration attacks. Lines 62-63 and 67-68 expose username. File: src/Framework.Api/Identity/Authentication/ApiKey/ApiKeyAuthenticationHandler.cs

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
- [ ] Replace specific messages with generic 'Authentication failed'
- [ ] Add tests to verify no PII in error responses

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
