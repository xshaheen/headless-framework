---
status: pending
priority: p1
issue_id: "008"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# Add startup validation for SensitiveDataStrategy.Transform with null SensitiveValueTransformer

## Problem Statement

When SensitiveDataStrategy = Transform is configured (globally or per-property via [AuditSensitive(Transform)]) but SensitiveValueTransformer is null, EfAuditChangeCapture._ApplySensitiveValues silently falls back to Redact (stores '***'). No log warning, no exception, no diagnostic. A developer who configures Transform intending custom HMAC-SHA256 pseudonymization for GDPR compliance but forgets to wire up the transformer gets silent Redact instead. Their audit log appears to work (no errors) but their compliance requirement (custom transform) is silently not met. The code path in question: EfAuditChangeCapture.cs lines 295-336.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:295-336
- **Options location:** src/Headless.AuditLog.Abstractions/AuditLogOptions.cs:32
- **Severity:** P1 — silent data protection misconfiguration in compliance context
- **Discovered by:** security-sentinel, code-simplicity-reviewer

## Proposed Solutions

### Add ValidateOnStart options validation
- **Pros**: Fails fast at startup before any SaveChanges can occur; standard .NET pattern
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

In Abstractions/Setup.cs: add `services.AddOptions<AuditLogOptions>().Validate(opts => opts.SensitiveDataStrategy != SensitiveDataStrategy.Transform || opts.SensitiveValueTransformer != null, "SensitiveValueTransformer must be set when SensitiveDataStrategy is Transform.").ValidateOnStart()`. Remove the dead else branch in _ApplySensitiveValues once validation is in place.

## Acceptance Criteria

- [ ] Application fails to start with descriptive error when Transform is set without a transformer
- [ ] Same validation applies when per-property [AuditSensitive(Transform)] is used
- [ ] Dead else-branch in _ApplySensitiveValues removed after validation is in place
- [ ] Unit test: startup throws OptionsValidationException when misconfigured

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
