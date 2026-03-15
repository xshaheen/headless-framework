---
status: pending
priority: p2
issue_id: "009"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Replace _FallbackToRedact exception filter with plain catch + logging

## Problem Statement

EfAuditChangeCapture uses `catch (Exception ex) when (_FallbackToRedact(ex))` where `_FallbackToRedact` always returns true. This is semantically identical to `catch (Exception)` but adds a confusing always-true method. More importantly, the catch block does not log the transformer failure — it silently swallows it and falls back to Redact. Developers get no feedback when their transformer throws (typo, null ref, etc.). The `_FallbackToRedact` method in EfAuditChangeCapture.cs:415 is dead abstraction: it provides no conditional filtering.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:316,415
- **Discovered by:** strict-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

### Replace with plain catch + LogWarning
- **Pros**: Developers see transformer failures; removes confusing indirection
- **Cons**: _ApplySensitiveValues is static and logger is not in scope — must pass logger or refactor to instance method
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Delete `_FallbackToRedact`. Replace `catch (Exception ex) when (_FallbackToRedact(ex))` with `catch (Exception ex)` and add a `logger.LogWarning(ex, "Sensitive value transformer threw for {Type}.{Property}. Falling back to Redact.", ...)`. Pass logger through to `_ApplySensitiveValues` or convert to an instance method.

## Acceptance Criteria

- [ ] _FallbackToRedact method deleted
- [ ] Transformer exceptions logged at Warning level with entity type and property name
- [ ] Test: transformer that throws produces Redact output AND a log warning

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
