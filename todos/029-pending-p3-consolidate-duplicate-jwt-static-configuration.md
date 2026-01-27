---
status: pending
priority: p3
issue_id: "029"
tags: []
dependencies: []
---

# Consolidate duplicate JWT static configuration

## Problem Statement

JsonWebTokenHandler.DefaultMapInboundClaims and DefaultInboundClaimTypeMap.Clear() are set in both Setup.cs ConfigureGlobalSettings() and JwtTokenHelper._CreateHandler(). Risk of mismatch if accessed in different order. Files: Setup.cs lines 39-42, JwtTokenHelper.cs lines 13-14

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
- [ ] Consolidate to single initialization point
- [ ] Document expected initialization order

## Notes

Source: Workflow automation

## Work Log

### 2026-01-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create
