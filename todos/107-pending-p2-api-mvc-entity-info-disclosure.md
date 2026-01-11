---
status: pending
priority: p2
issue_id: "107"
tags: [code-review, security, api-mvc, owasp]
dependencies: []
---

# Entity Key Exposure in Error Responses - Information Disclosure

## Problem Statement

The `EntityNotFound` problem details exposes both the entity name and search key in responses:
```json
{
  "title": "Entity Not Found",
  "detail": "User with key '12345' was not found",
  "params": { "entity": "User", "key": "12345" }
}
```

This reveals internal entity naming and key structures to potential attackers, aiding reconnaissance.

## Findings

**Source:** security-sentinel agent

**OWASP Category:** A01:2021 - Broken Access Control / A04:2021 - Insecure Design

**Location:** `src/Framework.Api/Abstractions/IProblemDetailsCreator.cs:59-72`

```csharp
Detail = HeadlessProblemDetailsConstants.Details.EntityNotFound(entity, key),
Extensions = { ["params"] = new { entity, key } },
```

**Impact:**
- Attackers can enumerate valid entity types
- Key format disclosure (GUID vs int vs string) aids SQL injection attempts
- Internal naming conventions leaked

## Proposed Solutions

### Option 1: Environment-Based Verbosity (Recommended)
**Pros:** Detailed in dev, generic in prod
**Cons:** Need configuration
**Effort:** Medium
**Risk:** Low

### Option 2: Always Generic Messages
**Pros:** Simple, secure by default
**Cons:** Harder debugging in all environments
**Effort:** Small
**Risk:** Low

## Acceptance Criteria

- [ ] Production responses don't expose entity/key details
- [ ] Development retains detailed messages for debugging
- [ ] Configuration documented

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Information disclosure in error responses |

## Resources

- IProblemDetailsCreator.cs
- OWASP A01:2021
