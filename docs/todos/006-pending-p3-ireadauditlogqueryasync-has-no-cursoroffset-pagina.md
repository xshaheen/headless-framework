---
status: pending
priority: p3
issue_id: "006"
tags: ["code-review","api-design","audit","ux"]
dependencies: []
---

# IReadAuditLog.QueryAsync has no cursor/offset pagination — unbounded for high-volume audit logs

## Problem Statement

IReadAuditLog.QueryAsync only supports a `limit` parameter with no cursor, offset, or keyset pagination. For audit logs that grow to millions of rows (normal in production), there is no way to page through results beyond the initial limit. Consumers cannot implement 'load more' or paginate audit history UIs without a page/offset parameter. The default limit=100 is arbitrary and can silently truncate results without signaling that more data exists. This is a v1 limitation but should be tracked for the roadmap.

## Findings

- **IReadAuditLog.QueryAsync signature:** src/Headless.AuditLog.Abstractions/IReadAuditLog.cs:35-47
- **EfReadAuditLog implementation:** src/Headless.AuditLog.EntityFramework/EfReadAuditLog.cs:40-43

## Proposed Solutions

### Add cursor-based pagination (keyset on CreatedAt, Id)
- **Pros**: Efficient on large tables. Works well with composite PK.
- **Cons**: More complex API. Breaking change to IReadAuditLog if added later.
- **Effort**: Medium
- **Risk**: Low

### Add offset-based pagination (skip + take)
- **Pros**: Simpler API. Familiar pattern.
- **Cons**: Performance degrades at high offsets. Non-deterministic on concurrent inserts.
- **Effort**: Small
- **Risk**: Low

### Document v1 limitation explicitly; add TODO comment in interface
- **Pros**: Sets expectations. No breaking change risk now.
- **Cons**: Defers the problem.
- **Effort**: Small
- **Risk**: Low


## Recommended Action

For v1, add a TODO in IReadAuditLog.QueryAsync documenting that pagination is a planned enhancement. Include a `beforeId` or `cursor` overload in the backlog. The current limit-only approach is acceptable for v1 given the framework nature of the package.

## Acceptance Criteria

- [ ] IReadAuditLog has pagination support OR a documented plan for adding it
- [ ] Default limit=100 behavior is clearly documented

## Notes

Discovered during PR #187 review. Low urgency for v1 but should be tracked before v1.0 release.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
