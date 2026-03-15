---
status: pending
priority: p2
issue_id: "018"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# Expose DefaultExcludedProperties as configurable option — hardcoded list not overridable

## Problem Statement

EfAuditChangeCapture._DefaultExcludedProperties is a private static readonly HashSet containing ConcurrencyStamp, DateCreated, DateUpdated, etc. These are excluded from audit for all consumers with no opt-out. A consumer with a legitimate property named DateCreated that they DO want to audit has no way to include it — PropertyFilter can only exclude, not re-include properties excluded by the static list. This violates the framework's principle of being unopinionated and zero lock-in.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:17-28
- **Discovered by:** pragmatic-dotnet-reviewer, security-sentinel

## Proposed Solutions

### Move to AuditLogOptions.DefaultExcludedProperties
- **Pros**: Consumers can clear or modify the set; still defaults to the current list
- **Cons**: Minor breaking change if anyone was relying on it being hardcoded
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add `public HashSet<string> DefaultExcludedProperties { get; set; } = new(StringComparer.Ordinal) { "ConcurrencyStamp", "DateCreated", ... }` to AuditLogOptions. EfAuditChangeCapture reads from options instead of the static field. Consumers can `options.DefaultExcludedProperties.Clear()` or add/remove entries.

## Acceptance Criteria

- [ ] DefaultExcludedProperties exposed on AuditLogOptions
- [ ] EfAuditChangeCapture reads from opts.DefaultExcludedProperties (not static field)
- [ ] Default value matches current hardcoded list
- [ ] Test: consumer can include previously-excluded property by removing from DefaultExcludedProperties

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
