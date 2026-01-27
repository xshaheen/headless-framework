---
status: pending
priority: p3
issue_id: "030"
tags: []
dependencies: []
---

# Add JWT signing key minimum length validation

## Problem Statement

JwtTokenFactory doesn't validate minimum key length. HMAC-SHA256 requires at least 256-bit (32 byte) keys for security. File: src/Framework.Api/Security/Jwt/IJwtTokenFactory.cs line 136

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
- [ ] Add key length validation in _CreateSecurityKey
- [ ] Throw ArgumentException for keys under 32 bytes

## Notes

Source: Workflow automation

## Work Log

### 2026-01-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create
