---
status: pending
priority: p2
issue_id: "015"
tags: [data-integrity, deduplication, idempotency, received-messages]
dependencies: []
---

# Missing Message Deduplication for Received Messages

## Problem Statement

Received messages table has no deduplication mechanism, allowing duplicate processing when brokers deliver messages multiple times (at-least-once semantics).

## Findings

**Root Cause**: No unique constraint on MessageId for received messages.

**Duplicate Scenarios**:
- Broker redelivery on connection loss
- Consumer acknowledgment timeout
- Manual reprocessing

**Impact**:
- Duplicate business logic execution
- Data corruption (double payments, duplicate orders)
- Idempotency burden on consumers

## Proposed Solutions

### Option 1: Unique Constraint + Upsert (RECOMMENDED)
**Effort**: 3-4 hours

```sql
ALTER TABLE received ADD CONSTRAINT uq_received_messageid_group
UNIQUE (MessageId, GroupName);
```

Update insert logic:
```csharp
// PostgreSQL
ON CONFLICT (MessageId, GroupName) DO UPDATE SET StatusName = EXCLUDED.StatusName;

// SQL Server
MERGE INTO received USING (...) ON received.MessageId = @msgId AND received.GroupName = @group;
```

### Option 2: Check Before Insert
**Effort**: 2-3 hours
**Risk**: Race conditions

Query before insert - NOT recommended (race condition window).

## Recommended Action

Implement Option 1 - database-level deduplication.

## Acceptance Criteria

- [ ] Unique constraint on (MessageId, GroupName)
- [ ] Insert logic handles constraint violations gracefully
- [ ] Integration test verifies duplicate detection
- [ ] Performance impact measured (<10% overhead)
- [ ] Metrics track duplicate rate

## Technical Details

**GroupName in Key**: Different consumer groups may process same message (pub-sub pattern).

**Migration Strategy**:
1. Add constraint to new databases
2. Existing databases: dedupe first, then add constraint

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Data Integrity Guardian Agent)

**Actions:**
- Identified missing deduplication
- Proposed database-level solution
- Analyzed at-least-once delivery semantics
