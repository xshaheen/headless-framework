---
status: pending
priority: p2
issue_id: "017"
tags: ["code-review","architecture","dotnet","agent-native"]
dependencies: []
---

# Add IReadAuditLog interface to Abstractions for querying audit entries

## Problem Statement

IAuditLogStore exposes only Save/SaveAsync. There is no corresponding query interface. Any consumer (agent, compliance tool, audit dashboard, integration test) doing read-back must bypass the abstraction layer entirely and query DbSet<AuditLogEntry> directly — coupling them to the EF implementation detail. If the store backend ever changes (Elasticsearch, read database, etc.), all consumers break with no contract to re-implement against.

## Findings

- **Location:** src/Headless.AuditLog.Abstractions/IAuditLogStore.cs
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Add IReadAuditLog to Abstractions package
- **Pros**: Stable query contract; decouples consumers from EF; enables agent-driven compliance reporting
- **Cons**: Additional interface to maintain; EF implementation must be added
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Add `IReadAuditLog` to Abstractions with `Task<IReadOnlyList<AuditLogEntryData>> QueryAsync(string? action, string? entityType, string? entityId, string? userId, string? tenantId, DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken ct)`. Implement in EF package as EfReadAuditLog querying DbSet<AuditLogEntry>.

## Acceptance Criteria

- [ ] IReadAuditLog interface added to Abstractions package
- [ ] EfReadAuditLog implementation in EntityFramework package
- [ ] Registered in AddHeadlessAuditLogEntityFramework
- [ ] Returns AuditLogEntryData (not AuditLogEntry) to avoid leaking EF entity

## Notes

Discovered during PR #187 code review. Agent-native reviewer flagged this as a key gap for programmatic audit log access.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
