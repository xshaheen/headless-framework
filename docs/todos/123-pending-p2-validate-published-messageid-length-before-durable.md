---
status: pending
priority: p2
issue_id: "123"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Validate published MessageId length before durable storage

## Problem Statement

PublishOptions.MessageId is documented as an arbitrary logical string, but both durable providers persist it into VARCHAR/NVARCHAR(200) columns without any validation or truncation. A caller can supply a longer logical ID and trigger a database exception during publish.

## Findings

- **Location:** src/Headless.Messaging.Abstractions/PublishOptions.cs:20
- **Location:** src/Headless.Messaging.Core/README.md:103
- **Location:** src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs:80
- **Location:** src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:116
- **Location:** src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs:94
- **Risk:** High - valid-looking custom MessageId values can fail at the database boundary
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Fail fast with a documented maximum length
- **Pros**: Clear behavior and no silent truncation
- **Cons**: Still imposes a caller-visible limit
- **Effort**: Small
- **Risk**: Low

### Increase the durable column size and document transport limits
- **Pros**: Accepts longer logical IDs without immediate failures
- **Cons**: Schema change and transport-specific limits still need clarity
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Pick an explicit MessageId limit and enforce it before persistence, or widen the durable schema deliberately; the current implicit DB failure path is not acceptable.

## Acceptance Criteria

- [ ] Published message creation rejects or safely stores overlong MessageId values before hitting the database
- [ ] The supported MessageId length is documented in code and README surface area
- [ ] Tests cover SQL Server and PostgreSQL durable publish paths with boundary-length MessageId values

## Notes

PR #198 code review on 2026-03-25

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
