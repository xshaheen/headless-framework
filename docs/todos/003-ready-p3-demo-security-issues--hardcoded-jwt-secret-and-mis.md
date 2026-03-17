---
status: ready
priority: p3
issue_id: "003"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# Demo security issues — hardcoded JWT secret and missing safety comments

## Problem Statement

JWT demo ships a real signing key in committed appsettings.json. RequireHttpsMetadata=false and ValidateIssuer/Audience=false are suppressed with #pragma but no explanatory comment. Developers copying this demo as a starting point will ship insecure defaults. Also: UnsafeRelaxedJsonEscaping used for inline script injection in Setup.cs should use JavaScriptEncoder.Default.

## Findings

- **JWT secret:** demo/Headless.Messaging.Dashboard.Jwt.Demo/appsettings.json
- **Pragma suppress:** demo/Headless.Messaging.Dashboard.Jwt.Demo/Program.cs:24,31,32
- **Unsafe encoder:** src/Headless.Messaging.Dashboard/Setup.cs:19-22
- **Discovered by:** security-sentinel

## Proposed Solutions

### Replace secret with placeholder + add DEMO ONLY comments + use safe encoder
- **Pros**: Low effort, prevents copy-paste security issues
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Replace JWT key with __REPLACE__ placeholder. Add prominent DEMO ONLY comment blocks. Switch to JavaScriptEncoder.Default.

## Acceptance Criteria

- [ ] No real secrets in committed config
- [ ] All security-disabled settings have DEMO ONLY comments
- [ ] Inline script JSON uses safe encoder

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
