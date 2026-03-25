---
status: done
priority: p2
issue_id: "120"
tags: ["code-review","dotnet","architecture","quality"]
dependencies: []
---

# Preserve pagination metadata in messaging dashboard list endpoints

## Problem Statement

PR #198 changes the published and received list endpoints to return only `{ items, totals }` instead of the full paged payload shape that callers previously received from `IndexPage<MessageView>`. That silently drops pagination metadata such as `index`, `size`, `totalItems`, `totalPages`, `hasPrevious`, and `hasNext` for any dashboard/API consumer, even though the plan only called for clarifying storage vs logical identity.

## Findings

- **Location:** src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs:421-476
- **Risk:** Medium - unversioned API contract regression for paged list consumers
- **Discovered by:** manual review (dev:code-review workflow)

## Proposed Solutions

### Option 1: Preserve the existing paged contract
- **Pros**: Keeps current consumers working while still exposing storageId/messageId on each item
- **Cons**: Requires shaping the mapped result as an IndexPage-compatible payload instead of a minimal anonymous object
- **Effort**: Small
- **Risk**: Low

### Option 2: Version the endpoint contract
- **Pros**: Allows the simplified payload if it is truly intentional
- **Cons**: Requires routing/docs/client migration work and is larger than this identity refactor
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Use Option 1 and keep the previous pagination metadata while mapping each item to the new storage/message identity shape.

## Acceptance Criteria

- [x] Published and received list endpoints keep the previous pagination metadata fields while exposing storageId and messageId per item
- [x] Unit or endpoint tests assert the list response contract for both published and received routes
- [x] Any intentional API contract change is explicitly documented instead of happening as an incidental side effect of the identity refactor

## Notes

Discovered during PR #198 review against docs/plans/2026-03-23-002-refactor-messaging-storage-message-identity-plan.md

## Work Log

### 2026-03-24 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-25 - Resolved

**By:** Codex
**Actions:**
- Restored the published and received list endpoints to emit the existing page metadata while keeping item-level `storageId` and `messageId`
- Kept a `totals` alias in the payload so the current dashboard client continues to work while the full pagination contract remains available
- Added unit coverage for both list endpoints and verified the new published/received list tests with targeted `dotnet test` runs

### 2026-03-24 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-25 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
