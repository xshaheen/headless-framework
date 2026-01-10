# Agent-Native Gaps in Resource Lock API

---

status: pending
priority: p3
issue_id: "003"
tags: [code-review, agent-native, api-design, enhancement]
dependencies: []

---

## Problem Statement

The Resource Lock API lacks some observability features that would make it fully "agent-native" - meaning an AI agent has the same capabilities as a human developer.

## Findings

**Identified by:** Agent-Native Reviewer

| Capability                 | User Access                                 | Agent Access  | Gap?    |
| -------------------------- | ------------------------------------------- | ------------- | ------- |
| Acquire/Release/Renew lock | API methods                                 | API methods   | No      |
| Check if locked            | `IsLockedAsync()`                           | API method    | No      |
| Get remaining TTL          | `IResourceLockStorage.GetExpirationAsync()` | Internal only | **YES** |
| List all active locks      | Not available                               | Not available | **YES** |
| Get lock holder info       | Not available                               | Not available | **YES** |
| Force release (admin)      | Not available                               | Not available | **YES** |

## Proposed Solutions

### Option A: Add GetExpirationAsync to provider interface (QUICK WIN)

```csharp
// In IResourceLockProvider
Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default);
```

**Pros:** Simple, storage already has this method
**Cons:** Minor API surface increase
**Effort:** Small
**Risk:** Low

### Option B: Defer to future enhancement

Document gaps and track as future feature request.

**Pros:** No immediate work
**Cons:** Agent-native gaps remain
**Effort:** None
**Risk:** None

### Option C: Full observability API

Add all 4 capabilities: expiration, enumeration, holder info, force release.

**Pros:** Complete agent-native parity
**Cons:** Significant API expansion, security considerations for force release
**Effort:** Large
**Risk:** Medium

## Recommended Action

(To be filled during triage)

## Technical Details

-   **Affected files:** `IResourceLockProvider.cs`, `ResourceLockProvider.cs`
-   **Components:** ResourceLocks.Abstractions, ResourceLocks.Core

## Acceptance Criteria

-   [ ] Decision on which capabilities to add
-   [ ] Implementation (if approved)
-   [ ] Documentation updated

## Work Log

| Date       | Action  | Notes               |
| ---------- | ------- | ------------------- |
| 2026-01-10 | Created | Code review finding |

## Resources

-   PR #138: https://github.com/xshaheen/headless-framework/pull/138
