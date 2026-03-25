---
status: done
priority: p2
issue_id: "006"
tags: ["code-review","security","documentation"]
dependencies: []
---

# Correct messaging dashboard auth guidance

## Problem Statement

The regenerated messaging docs claim consumers must set `AllowAnonymousExplicit` or `AuthorizationPolicy`, but the actual dashboard builder uses `AuthMode.None` by default and exposes a different auth API (`WithNoAuth`, `WithHostAuthentication`, etc.). As written, the docs can mislead consumers about the real anonymous-default behavior and the available secure configuration options.

## Findings

- **Location:** docs/llms/messaging.md:697-751; src/Headless.Messaging.Dashboard/MessagingDashboardOptionsBuilder.cs:60-98; src/Headless.Dashboard.Authentication/AuthConfig.cs:11; src/Headless.Messaging.Dashboard/Setup.cs:69-75
- **Risk:** Consumers can misunderstand or misconfigure dashboard authentication in production
- **Discovered by:** code review

## Proposed Solutions

### Regenerate docs from actual builder API
- **Pros**: Keeps examples aligned to code
- **Cons**: Requires doc-source cleanup
- **Effort**: Small
- **Risk**: Low

### Add explicit security note + example matrix
- **Pros**: Improves safe adoption guidance
- **Cons**: More documentation maintenance
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Rewrite the dashboard auth section to reflect the real builder methods and explicitly call out that the default mode is anonymous unless auth is configured.

## Acceptance Criteria

- [x] Messaging dashboard docs describe the real auth API
- [x] Docs clearly state the default auth mode and production-safe options
- [x] Examples compile against the current codebase

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-25 - Implemented

**By:** Agent
**Actions:**
- Rewrote the dashboard auth guidance in generated messaging docs to match the real fluent API
- Updated examples to use `WithBasicAuth`, `WithHostAuthentication`, `WithApiKey`, and `WithCustomAuth`
- Corrected the package README custom-auth example to match the current delegate signature

### 2026-03-25 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
