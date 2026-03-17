---
title: "feat: Add stack trace persistence and display for failed received messages"
type: feat
date: 2026-03-17
---

> **Verification gate:** Before claiming any task or story complete — run the plan's `verification_command` and confirm PASS. Do not mark complete based on reading code alone.

# feat: Add stack trace persistence and display for failed received messages

## Overview

When a subscriber fails processing a received message, only `"ExceptionType-->Message"` is stored in `Headers["headless-exception"]`. The full stack trace is completely lost. The dashboard's `MessageDetailDialog` has existing (dead) exception rendering code that never fires because the data never arrives.

This feature adds a dedicated `ExceptionInfo` column to the Received table, captures `ex.ToString()` on failure, and displays it in a dedicated tab in the message detail dialog.

## Problem Statement / Motivation

Debugging failed messages currently requires reproducing the failure locally because the stack trace is discarded at the persistence layer. The dashboard shows the message payload but gives no indication of _why_ it failed beyond a type name and short message. This makes the dashboard ineffective for failure triage.

## Proposed Solution

1. Add `ExceptionInfo TEXT NULL` column to the Received table (SqlServer + PostgreSql)
2. Add `ExceptionInfo` property to `MediumMessage`
3. Capture `ex.ToString()` in both failure paths (subscriber execution + deserialization)
4. Persist via updated storage provider methods
5. Surface via dashboard API endpoint
6. Display in a dedicated "Exception" tab in `MessageDetailDialog`

## Technical Considerations

### Schema Migration

No migration framework exists — schema is managed via idempotent DDL in `IStorageInitializer.InitializeAsync()`. New column requires:
- Updated `CREATE TABLE` for fresh installs
- `ALTER TABLE ... ADD COLUMN` with existence guards for upgrades
- SQL Server: `IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE ...)` guard
- PostgreSQL: `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`

### Shared `_ChangeMessageStateAsync` Must Be Split

`_ChangeMessageStateAsync` is a private helper shared by `ChangePublishStateAsync` and `ChangeReceiveStateAsync` in both SqlServer and PostgreSql providers. Since `ExceptionInfo` only exists on the Received table, `ChangeReceiveStateAsync` needs its own SQL path that includes the column. `ChangePublishStateAsync` keeps the existing shared SQL.

### `StoreReceivedExceptionMessageAsync` Signature Change

This is on `IDataStorage` (public interface). Adding `string? exceptionInfo = null` as a default parameter is backward-compatible for callers. The exception variable `e` at `IConsumerRegister.cs:250` is out of scope by the time `StoreReceivedExceptionMessageAsync` is called at line 283. Solution: declare `string? exceptionInfo = null` before the try block, set it in the catch.

### `_StoreReceivedMessage` MERGE Statement

The MERGE in SqlServer/PostgreSql has explicit column lists in both UPDATE and INSERT clauses — must include `ExceptionInfo` in both branches.

### Ordinal Column Reads in `GetMessagesAsync`

`SELECT *` with `index++` ordinal reads. Adding `ExceptionInfo` as the **last column** (after `MessageId`) keeps existing ordinal positions stable. The list view reader stops at `StatusName` (index 8) and never reads `MessageId` (index 9) or beyond — safe.

### `Headers.Exception` — Keep As-Is

`Headers.Exception` ("Type-->Message") is kept for backward compatibility. `ExceptionInfo` is additive. `HasException()` check in `IConsumerRegister` still depends on it.

### Retry Behavior

Each `_SetFailedState` call overwrites `ExceptionInfo` with the latest exception. Only the last retry's trace is persisted. This is consistent with how `Content` and `Retries` are updated.

### Frontend Typecheck

All Vue/TS changes must pass `vue-tsc --build`. The `MessageDetail` interface must be properly extended, and the new tab logic must be type-safe.

## Stories

> Full story details in companion PRD: [`2026-03-17-feat-received-exception-stack-trace-plan.prd.json`](./2026-03-17-feat-received-exception-stack-trace-plan.prd.json)

| ID | Title | Size |
|----|-------|------|
| US-001 | Add `ExceptionInfo` property to `MediumMessage` | XS |
| US-002 | Add `ExceptionInfo` column to database schemas | M |
| US-003 | Capture and persist exception info in failure paths | M |
| US-004 | Update monitoring APIs to read `ExceptionInfo` | S |
| US-005 | Update dashboard endpoint to return `ExceptionInfo` | XS |
| US-006 | Add Exception tab to `MessageDetailDialog` | M |

## Success Metrics

- Failed received messages show full `ex.ToString()` output in the dashboard
- Existing messages without `ExceptionInfo` display gracefully (null/empty)
- Schema upgrade is idempotent — restarting the app doesn't break
- Frontend passes `vue-tsc --build`

## Dependencies & Risks

- **Breaking interface change**: `IDataStorage.StoreReceivedExceptionMessageAsync` gains a new default parameter. Backward-compatible but all 3 implementations must update.
- **Shared SQL split**: Splitting `_ChangeMessageStateAsync` in SqlServer/PostgreSql introduces minor code duplication.
- **Large exceptions**: No size cap is applied. Deeply nested `AggregateException.ToString()` could produce very large strings. Acceptable for a framework-internal dashboard tool.

## Sources & References

- `SubscriberExecutionFailedException` already proxies `StackTrace` from the original exception: `src/Headless.Messaging.Core/Internal/SubscriberExecutionFailedException.cs`
- Failure path A (deserialization): `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:250`
- Failure path B (subscriber execution): `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs:175`
- Dead exception rendering code: `src/Headless.Messaging.Dashboard/wwwroot/src/components/MessageDetailDialog.vue:54-98`
