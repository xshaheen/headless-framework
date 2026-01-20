---
status: ready
priority: p1
issue_id: "011"
tags: [data-integrity, outbox-pattern, transactions, race-condition]
dependencies: []
---

# Transaction Commit/Flush Race Condition (Data Loss Risk)

## Problem Statement

**CRITICAL DATA INTEGRITY ISSUE**: Outbox transaction implementations commit database changes BEFORE flushing messages, violating the outbox pattern and creating data loss scenarios.

Affected files:
- `src/Framework.Messages.PostgreSql/PostgreSqlOutboxTransaction.cs:35-50`
- `src/Framework.Messages.SqlServer/SqlServerOutboxTransaction.cs` (similar pattern)

## Findings

**Root Cause**: CommitAsync() executes database commit before message flush, breaking atomicity.

**Vulnerable Code** (PostgreSqlOutboxTransaction.cs:35-50):
```csharp
public override async Task CommitAsync(CancellationToken cancellationToken = default)
{
    await dbTransaction.CommitAsync(cancellationToken); // DB commits FIRST
    await FlushAsync();  // Messages flushed AFTER - NOT ATOMIC!
}
```

**Data Loss Scenario**:
1. User performs business operation (e.g., create order)
2. Transaction commits to database (order saved)
3. `FlushAsync()` fails (network issue, broker down, exception)
4. **Result**: Order saved, but OrderCreated message never published
5. Downstream services never notified → data inconsistency

**Why This Violates Outbox Pattern**:
- Outbox guarantees at-least-once delivery by storing messages in same transaction
- Messages should be persisted BEFORE commit
- Flush can fail without affecting commit

## Proposed Solutions

### Option 1: Reverse Order - Flush Before Commit (RECOMMENDED)
**Effort**: 30 minutes
**Risk**: Low
**Correctness**: HIGH

```csharp
public override async Task CommitAsync(CancellationToken cancellationToken = default)
{
    await FlushAsync(); // Flush messages FIRST (still in transaction)
    await dbTransaction.CommitAsync(cancellationToken); // Commit LAST
}
```

**Rationale**:
- If flush fails, transaction rolls back → no data loss
- If commit fails after flush, messages stay in outbox → will retry
- Maintains atomicity guarantee

### Option 2: Make Flush Idempotent + Retry Logic
**Effort**: 2-3 hours
**Risk**: Medium
**Complexity**: Higher

Add retry mechanism:
```csharp
public override async Task CommitAsync(CancellationToken cancellationToken = default)
{
    await dbTransaction.CommitAsync(cancellationToken);

    // Retry flush with backoff
    await Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
        .ExecuteAsync(() => FlushAsync());
}
```

**Problem**: Still violates outbox semantics - messages can be lost if all retries fail.

## Recommended Action

**Implement Option 1** - simple, correct, follows outbox pattern semantics.

**Implementation Steps**:
1. Swap order in PostgreSqlOutboxTransaction.cs
2. Swap order in SqlServerOutboxTransaction.cs
3. Add integration test verifying rollback on flush failure
4. Update documentation explaining order guarantee

## Acceptance Criteria

- [ ] FlushAsync() called BEFORE CommitAsync() in all outbox implementations
- [ ] Integration test verifies transaction rollback when flush fails
- [ ] Integration test verifies successful commit when flush succeeds
- [ ] No data loss scenario possible (messages persisted in same transaction)
- [ ] Documentation updated explaining atomic guarantee

## Technical Details

**Current Flow** (WRONG):
```
1. Business logic executes
2. Messages queued in memory
3. DB transaction commits ✅
4. Flush messages to outbox table ❌ (can fail)
```

**Correct Flow**:
```
1. Business logic executes
2. Messages queued in memory
3. Flush messages to outbox table (in transaction)
4. DB transaction commits (atomic: business data + messages)
```

**Testing Strategy**:
- Simulate flush failure (broker down)
- Verify business transaction rolls back
- Verify messages not committed
- Simulate successful flush
- Verify both business data and messages committed

## Resources

- [Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html)
- [Transactional Messaging](https://docs.microsoft.com/en-us/azure/architecture/patterns/outbox)

## Notes

This is a **fundamental correctness issue** in the outbox pattern implementation.

The outbox pattern exists specifically to prevent this class of data loss. Current implementation defeats the purpose.

**Impact Assessment**:
- Low-traffic systems: Rare (flush usually succeeds)
- High-traffic systems: Regular data loss during broker outages
- Distributed systems: Cascading failures from missing events

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Data Integrity Guardian Agent)

**Actions:**
- Identified during transaction flow analysis
- Analyzed outbox pattern violation
- Proposed order reversal fix

**Severity Justification**:
- Data loss scenario
- Violates core pattern semantics
- Simple fix available

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
