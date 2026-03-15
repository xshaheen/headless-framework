---
status: done
priority: p2
issue_id: "002"
tags: ["code-review","bug","data-integrity","audit"]
dependencies: []
---

# UserAgent (and other string fields) lack truncation guard, can cause DB column overflow

## Problem Statement

EfAuditLogStore and EfAuditLog only truncate CorrelationId at 128 chars, but all other bounded string columns (UserAgent max 512, EntityId max 256, EntityType max 512, UserId max 128, Action max 256) have no truncation logic. UserAgent strings can exceed 512 chars in browsers with many extensions installed. If a consumer passes an over-length UserAgent (a realistic external input), the DB insert will throw a DbUpdateException with a column size violation, causing the entire SaveChanges to fail. This is inconsistent with CorrelationId's defensive truncation.

## Findings

- **EfAuditLogStore CorrelationId truncation only:** src/Headless.AuditLog.EntityFramework/EfAuditLogStore.cs:64-65
- **EfAuditLog CorrelationId truncation only:** src/Headless.AuditLog.EntityFramework/EfAuditLog.cs:41-43
- **UserAgent max 512 defined:** src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs:32
- **EntityId max 256 defined:** src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs:27

## Proposed Solutions

### Add truncation helper and apply consistently to all bounded string fields
- **Pros**: Consistent defensive behavior. Prevents hard failures from external data.
- **Cons**: Silently truncates data which may obscure issues.
- **Effort**: Small
- **Risk**: Low

### Add validation on AuditLogEntryData before persisting
- **Pros**: Fails fast with meaningful error instead of DB error.
- **Cons**: Changes error handling behavior. May break existing implicit truncation assumption for CorrelationId.
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Extract a Truncate(string? value, int maxLength) helper and apply it to UserAgent, IpAddress, Action, EntityType, EntityId, UserId, AccountId, TenantId when mapping in both EfAuditLogStore._AddEntries and EfAuditLog.LogAsync. This mirrors the existing CorrelationId truncation pattern.

## Acceptance Criteria

- [ ] All string fields with HasMaxLength configuration have corresponding truncation in EfAuditLogStore._AddEntries
- [ ] All string fields with HasMaxLength have truncation in EfAuditLog.LogAsync
- [ ] Unit test covers UserAgent > 512 chars producing truncated (not throwing) output

## Notes

Discovered during PR #187 review. UserAgent is the most realistic consumer-provided field that can exceed its limit. CorrelationId already has the truncation pattern to follow.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-15 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
