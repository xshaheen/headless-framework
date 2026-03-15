---
status: pending
priority: p2
issue_id: "003"
tags: ["code-review","quality","dotnet","entity-framework"]
dependencies: []
---

# Make ConfigureAuditLog portable on SQLite or document the provider caveat

## Problem Statement

The public default mapping uses a composite primary key of { CreatedAt, Id } with Id.ValueGeneratedOnAdd. SQLite cannot generate values for that shape, and the only integration tests pass because the fixture overrides the mapping to a single-column key. The README quick start presents ConfigureAuditLog() as a universal default, which is false today for SQLite consumers.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs:18-22
- **Location:** tests/Headless.AuditLog.EntityFramework.Tests.Integration/Fixture/AuditTestDbContext.cs:19-27
- **Location:** src/Headless.AuditLog.EntityFramework/README.md:41-48
- **Risk:** Medium - users following the documented default path on SQLite will hit a runtime schema/save failure

## Proposed Solutions

### Provider-aware key strategy
- **Pros**: Keeps the package working out of the box across supported EF providers
- **Cons**: Requires provider detection or a different key design
- **Effort**: Medium
- **Risk**: Low

### Document the SQLite override explicitly
- **Pros**: Small change and immediately removes the misleading quick start
- **Cons**: Leaves SQLite support as an opt-in workaround instead of a real default
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Either change the default key design to one that works on SQLite too, or explicitly document and test the required SQLite override instead of implying universal portability.

## Acceptance Criteria

- [ ] The documented quick start works on SQLite, or the README and XML docs clearly call out the required override
- [ ] An automated test covers the default mapping behavior on SQLite
- [ ] Provider-specific behavior is intentional and discoverable

## Notes

The test fixture comment already documents the limitation locally, but the product README does not.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
