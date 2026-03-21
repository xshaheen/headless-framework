---
status: pending
priority: p2
issue_id: "014"
tags: ["code-review","dotnet","architecture"]
dependencies: []
---

# Add CancellationToken to HeadlessIdentityDbContext.ExecuteTransactionAsync overloads

## Problem Statement

HeadlessDbContext.ExecuteTransactionAsync has CancellationToken parameters on all 4 overloads (lines 135, 185, 236, 289). HeadlessIdentityDbContext.ExecuteTransactionAsync is missing CancellationToken on all 4 overloads (lines 153, 198, 244, 292). This is API surface divergence on two classes with identical public contracts — consumers of Identity contexts cannot pass cancellation tokens to transaction operations.

## Findings

- **Missing CancellationToken in:** src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs:153, 198, 244, 292
- **Has CancellationToken in:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:135, 185, 236, 289
- **Discovered by:** pattern-recognition-specialist

## Proposed Solutions

### Add CancellationToken = default to all 4 HeadlessIdentityDbContext.ExecuteTransactionAsync overloads
- **Pros**: Restores API parity, non-breaking (default value)
- **Cons**: Minor — 4 signature changes
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add CancellationToken cancellationToken = default parameter to all 4 ExecuteTransactionAsync overloads in HeadlessIdentityDbContext, mirroring HeadlessDbContext signatures exactly.

## Acceptance Criteria

- [ ] All 4 HeadlessIdentityDbContext.ExecuteTransactionAsync overloads accept CancellationToken
- [ ] CancellationToken is forwarded to ExecuteAsync and BeginTransactionAsync calls within each overload
- [ ] Public API is identical between HeadlessDbContext and HeadlessIdentityDbContext for this method family

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
