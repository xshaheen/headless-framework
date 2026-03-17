---
status: done
priority: p2
issue_id: "016"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# AuthConfig.CustomValidator Func closure risk as singleton + AuthResult mutable properties

## Problem Statement

Two auth design issues: (1) AuthConfig.CustomValidator is Func<string, bool> registered as singleton. User closures can capture scoped services (DbContext, HttpContext) causing runtime failures. (2) AuthResult has mutable {get;set;} properties but is only constructed via factory methods — should use {get;init;} per project convention.

## Findings

- **CustomValidator:** src/Headless.Dashboard.Authentication/AuthConfig.cs:26
- **AuthResult:** src/Headless.Dashboard.Authentication/IAuthService.cs:24-35
- **Missing sealed:** AuthConfig, AuthResult, AuthInfo, AuthMiddleware, AuthService all missing sealed
- **Discovered by:** pragmatic-dotnet-reviewer, strict-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

### Change to interface or Func<string, IServiceProvider, bool>; use init-only props; seal all types
- **Pros**: Safer DI, immutable results, matches project conventions
- **Cons**: Breaking change for CustomValidator signature
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change CustomValidator to Func<string, IServiceProvider, bool> or IDashboardAuthValidator interface. Use init-only props on AuthResult. Seal all concrete types.

## Acceptance Criteria

- [ ] CustomValidator cannot capture scoped services unsafely
- [ ] AuthResult properties are init-only
- [ ] All auth types are sealed

## Notes

Source: Code review

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-17 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-17 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
