---
status: ready
priority: p1
issue_id: "006"
tags: [data-integrity, idempotency, documentation, critical]
dependencies: []
---

# Document Idempotency Requirement

CRITICAL data duplication risk - no protection against duplicate message delivery.

## Problem Statement

MassTransit provides **at-least-once delivery**, meaning messages can be duplicated on network failures, broker failover, or redelivery timeouts. No deduplication mechanism exists in the adapter. Handlers will receive same message multiple times, causing data corruption if not idempotent.

**Location:** Framework-wide - affects all message handlers

**Why Critical:** Data corruption in production under normal failure scenarios:
- Double-charging customers
- Duplicate inventory deductions
- Repeated side effects (emails, notifications)
- Inconsistent state

## Findings

**From data-integrity-guardian:**

**Data corruption scenario:**
```csharp
// Handler that's NOT idempotent
await messageBus.SubscribeAsync<OrderPlaced>(async order =>
{
    // Run twice on duplicate delivery
    await db.Inventory.UpdateAsync(i => i.Quantity -= order.Quantity);  // ❌ Double deduction
    await paymentProcessor.ChargeCard(order.Amount);                     // ❌ Charge twice
});
```

**When duplicates occur:**
- Network failures during acknowledgment
- Broker failover/restart
- Redelivery after timeout
- Consumer crash before ack
- Normal distributed systems behavior

**Evidence:**
- MassTransit docs confirm at-least-once delivery
- No deduplication in `MassTransitMessageBusAdapter`
- `UniqueId` available in `MessageSubscribeMedium` but not enforced
- No guidance in README about idempotency

## Proposed Solutions

### Option 1: Document Idempotency Requirement (Immediate)

**Approach:** Add clear documentation with code examples.

**README addition:**

```markdown
## Message Idempotency

**CRITICAL**: MassTransit provides at-least-once delivery. Messages may be duplicated during network failures or broker restarts. **All message handlers MUST be idempotent.**

### Implementing Idempotency

Use the `UniqueId` from `IMessageSubscribeMedium<T>` to detect duplicates:

```csharp
await messageBus.SubscribeAsync<OrderPlaced>(async (medium, ct) =>
{
    var messageId = medium.UniqueId;

    // Check if already processed
    if (await db.ProcessedMessages.AnyAsync(m => m.MessageId == messageId, ct))
    {
        _logger.LogInformation("Duplicate message {MessageId}, skipping", messageId);
        return; // Already processed
    }

    using var transaction = await db.Database.BeginTransactionAsync(ct);
    try
    {
        // Process business logic
        await db.Inventory.UpdateAsync(...);
        await paymentProcessor.ChargeCard(...);

        // Record message as processed
        await db.ProcessedMessages.AddAsync(new ProcessedMessage
        {
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow
        }, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw; // MassTransit will retry
    }
}, cancellationToken);
```

### Database Table

```sql
CREATE TABLE ProcessedMessages (
    MessageId UUID PRIMARY KEY,
    ProcessedAt TIMESTAMP NOT NULL,
    ExpiresAt TIMESTAMP -- Optional: for cleanup
);

CREATE INDEX IX_ProcessedMessages_ExpiresAt ON ProcessedMessages(ExpiresAt);
```

### Cleanup

Periodically remove old entries:

```csharp
await db.ProcessedMessages
    .Where(m => m.ProcessedAt < DateTime.UtcNow.AddDays(-7))
    .DeleteAsync();
```
```

**Pros:**
- Prevents data corruption immediately
- Users understand requirement
- Code examples guide implementation
- Low effort

**Cons:**
- Every user must implement
- Easy to forget/skip
- No compile-time enforcement

**Effort:** 1-2 hours (documentation)

**Risk:** Low - documentation change

---

### Option 2: Add Idempotency Helper (Future Enhancement)

**Approach:** Provide optional deduplication middleware.

```csharp
public interface IIdempotencyStore
{
    Task<bool> WasProcessedAsync(Guid messageId, CancellationToken ct);
    Task MarkProcessedAsync(Guid messageId, CancellationToken ct);
}

// Usage
services.AddHeadlessMassTransitAdapter(options =>
{
    options.EnableIdempotency<EfCoreIdempotencyStore>();
});

await messageBus.SubscribeAsync<Order>(async (medium, ct) =>
{
    // Deduplication handled automatically
    await ProcessOrder(medium.Payload);
});
```

**Pros:**
- Framework-level protection
- Opt-in behavior
- Reduces user error

**Cons:**
- Significant implementation effort
- Performance overhead (DB lookup per message)
- Storage management complexity

**Effort:** 1-2 weeks

**Risk:** Medium - needs careful design

---

### Option 3: Do Nothing (Not Recommended)

**Approach:** Assume users know about idempotency.

**Pros:**
- No work

**Cons:**
- Users WILL lose data
- Support burden
- Reputation damage

**Risk:** CRITICAL - production data corruption

## Recommended Action

**Implement Option 1 immediately** - Document idempotency requirement with examples.

**Implementation steps:**

1. Add "Message Idempotency" section to README (high priority)
2. Include code examples showing:
   - Duplicate detection using `UniqueId`
   - Database table schema
   - Transaction boundary best practices
   - Cleanup strategies
3. Add warning callout box emphasizing criticality
4. Link to MassTransit documentation on at-least-once delivery
5. Consider Option 2 as future enhancement (v2.0)

**Consider for future:**
- Option 2 as optional middleware
- Integration with distributed cache (Redis) for faster lookups
- Automatic cleanup of old message IDs

## Technical Details

**Affected files:**
- `src/Framework.Messaging.MassTransit/README.md` - add idempotency section
- No code changes required (documentation only)

**Storage considerations:**
- ProcessedMessages table size grows with message volume
- Need retention policy (7-30 days typical)
- Index on MessageId for fast lookups
- Consider partitioning for high volume

**Performance impact:**
- DB lookup on every message (SELECT by PK = fast)
- INSERT after processing (within transaction)
- Periodic DELETE for cleanup

**Alternative stores:**
- Redis (faster, TTL built-in)
- In-memory cache (not durable, risky)
- Distributed cache (balance of speed/durability)

## Resources

- **PR:** #136 (MassTransit adapter)
- **Review:** data-integrity-guardian
- **MassTransit:** [At-least-once delivery](https://masstransit.io/documentation/concepts/messages#message-delivery)
- **Pattern:** [Idempotent Consumer](https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html)

## Acceptance Criteria

- [ ] README has "Message Idempotency" section with warning
- [ ] Code example shows duplicate detection
- [ ] Database schema documented
- [ ] Transaction boundary best practices shown
- [ ] Cleanup strategy documented
- [ ] Link to MassTransit docs on delivery guarantees
- [ ] Warning callout emphasizes criticality

## Notes

- **Severity:** CRITICAL - data corruption risk
- **Impact:** Duplicate charges, inventory errors, repeated side effects
- **Timeline:** MUST document before merging PR #136
- **User responsibility:** Every handler must be idempotent
- **Quote from review:** "MassTransit provides at-least-once delivery. Messages can be duplicated on network failures, broker failover, or redelivery timeouts."

## Work Log

### 2026-01-16 - Initial Discovery

**By:** Claude Code (data-integrity-guardian agent)

**Actions:**
- Identified lack of idempotency protection during review
- Analyzed MassTransit delivery guarantees
- Documented data corruption scenarios
- Drafted comprehensive documentation with examples

**Learnings:**
- At-least-once delivery is MassTransit default
- Duplicates common in distributed systems
- UniqueId already available but not documented
- Users need explicit guidance with code samples

### 2026-01-16 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
