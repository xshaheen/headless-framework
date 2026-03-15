---
status: pending
priority: p2
issue_id: "013"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# IpAddress and UserAgent fields on AuditLogEntry always null — wire up or remove

## Problem Statement

AuditLogEntryData.cs and AuditLogEntry.cs both expose IpAddress and UserAgent fields. EfAuditChangeCapture never populates them — they are always null for entity-change audit entries. EfAuditLog.LogAsync also does not populate them. Consumers will see these fields on the entity and assume they are populated, only to find them always null. Either wire them up via IHttpContextAccessor in the capture pipeline, or remove them from this version and document them as reserved/future.

## Findings

- **Location:** src/Headless.AuditLog.Abstractions/AuditLogEntryData.cs:22-25, src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs
- **Severity:** Always-null fields create false expectations
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Option A: Wire up via IHttpContextAccessor
- **Pros**: Useful data captured automatically
- **Cons**: Adds HttpContext dependency to the EF package; not all callers are HTTP
- **Effort**: Medium
- **Risk**: Medium

### Option B: Remove fields from this version, document as future
- **Pros**: Clean API — no confusing always-null fields
- **Cons**: Breaking if consumers have already referenced these fields
- **Effort**: Small
- **Risk**: Low

### Option C: Keep fields, add XML doc saying they require consumer population
- **Pros**: No breaking change; consumers who want IP can populate via PropertyFilter or IAuditLog.LogAsync
- **Cons**: Still always null for auto-capture
- **Effort**: Trivial
- **Risk**: Low


## Recommended Action

Option C short-term: add XML docs on IpAddress and UserAgent stating they are not populated by automatic capture and must be populated by consumers via explicit IAuditLog.LogAsync calls or a custom IAuditChangeCapture implementation.

## Acceptance Criteria

- [ ] IpAddress and UserAgent have clear XML docs explaining they are not auto-populated
- [ ] README updated to reflect this limitation
- [ ] OR: both fields wired up via IHttpContextAccessor with proper null guard for non-HTTP scenarios

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
