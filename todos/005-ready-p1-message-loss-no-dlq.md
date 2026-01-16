---
status: ready
priority: p1
issue_id: "005"
tags: [data-integrity, messaging, masstransit, critical-bug]
dependencies: []
---

# Fix Message Loss - Configure Dead Letter Queue

CRITICAL data loss risk - messages permanently discarded after retry exhaustion.

## Problem Statement

When handler throws exception, message is re-thrown to MassTransit which permanently rejects after default retries (typically 5 attempts over ~30 seconds). No Dead Letter Queue configured means messages are **permanently lost** with no recovery mechanism.

**Location:** `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:216-244`

**Why Critical:** Production data loss under normal failure scenarios (database timeouts, network issues, deployment restarts). Once retries exhausted, message gone forever.

## Findings

**From data-integrity-guardian:**

```csharp
public async Task Consume(ConsumeContext<TPayload> ctx)
{
    try
    {
        await handler(medium, ctx.CancellationToken).AnyContext();
    }
    catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
    {
        throw;  // ❌ Message lost on cancellation
    }
    catch (Exception e)
    {
        logger.LogError(e, "Error processing {MessageType}", typeof(TPayload).Name);
        throw;  // ❌ Message lost after retries exhausted
    }
}
```

**Data loss scenario:**
1. Handler encounters transient database error (connection timeout)
2. Exception thrown → MassTransit retries 5 times
3. All retries fail (DB still unavailable)
4. Message permanently discarded
5. NO recovery mechanism, NO poison message queue

**Impact:**
- Lost business transactions
- Missing audit trail
- Data inconsistency
- Cannot replay or recover

## Proposed Solutions

### Option 1: Configure Error Queue in MassTransit Setup (Recommended)

**Approach:** Add error queue configuration to bus setup.

```csharp
// In MassTransit configuration (where AddMassTransit is called)
cfg.ReceiveEndpoint("error-queue", e =>
{
    e.ConfigureConsumeTopology = false;
});

// Configure retry policy
cfg.UseMessageRetry(r =>
{
    r.Interval(5, TimeSpan.FromSeconds(10));
    r.Handle<Exception>();
});

// Add delayed redelivery for transient failures
cfg.UseDelayedRedelivery(r => r.Intervals(
    TimeSpan.FromMinutes(5),
    TimeSpan.FromMinutes(15),
    TimeSpan.FromMinutes(30)
));
```

**Pros:**
- Messages preserved after retry exhaustion
- Can inspect/replay failed messages
- Standard MassTransit pattern
- Configurable retry behavior

**Cons:**
- Requires infrastructure setup (error queue)
- Need monitoring/alerting on error queue

**Effort:** 2-3 hours (config + testing)

**Risk:** Low - well-documented MassTransit pattern

---

### Option 2: Implement Custom Fault Handling

**Approach:** Add fault consumer to handle failures.

```csharp
cfg.ReceiveEndpoint("fault-queue", e =>
{
    e.Consumer<FaultConsumer>();
});

public class FaultConsumer : IConsumer<Fault>
{
    public Task Consume(ConsumeContext<Fault> context)
    {
        _logger.LogCritical(
            "Message processing failed: {MessageType}, Reason: {Reason}",
            context.Message.FailedMessage.MessageType,
            context.Message.Exceptions.First().Message
        );

        // Optional: store in DB for later replay
        return Task.CompletedTask;
    }
}
```

**Pros:**
- Programmatic fault handling
- Can implement custom recovery logic
- Database storage for replay

**Cons:**
- More code to maintain
- Need fault consumer implementation

**Effort:** 4-5 hours

**Risk:** Medium - requires testing fault scenarios

---

### Option 3: Document User Responsibility

**Approach:** Document that users must implement idempotency and handle retries.

**Pros:**
- No framework changes

**Cons:**
- Data loss still possible
- Pushes complexity to every user
- Easy to get wrong

**Effort:** 1 hour (documentation)

**Risk:** HIGH - users will lose data

**Not recommended** - Framework should provide safety

## Recommended Action

**Implement Option 1** - Configure error queue with retry policies.

**Implementation steps:**

1. Document DLQ configuration in README
2. Provide code example for retry policy setup
3. Add integration test for message retry behavior
4. Add test for error queue routing
5. Document monitoring/alerting recommendations

**Example documentation:**

```markdown
## Message Retry and Error Handling

MassTransit adapter requires error queue configuration to prevent message loss:

```csharp
services.AddMassTransit(x =>
{
    // Error queue for failed messages
    x.AddConfigureEndpointsCallback((name, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Immediate(5));
        cfg.UseDelayedRedelivery(r => r.Intervals(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15)
        ));
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.ConfigureEndpoints(ctx);

        // Dead letter queue
        cfg.ReceiveEndpoint("error", e =>
        {
            e.ConfigureConsumeTopology = false;
        });
    });
});
```

Monitor the error queue for failed messages.
```

## Technical Details

**Affected files:**
- `src/Framework.Messaging.MassTransit/README.md` - add DLQ documentation
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:238-243` - exception handling
- `tests/Framework.Messaging.MassTransit.Tests.Integration/` - add retry/error tests

**MassTransit configuration points:**
- `UseMessageRetry` - immediate retry policy
- `UseDelayedRedelivery` - scheduled redelivery
- Error queue endpoint
- Fault consumer (optional)

**Testing requirements:**
- Test message retry on handler exception
- Verify message routed to error queue after exhaustion
- Test delayed redelivery intervals
- Validate no message loss under failure scenarios

## Resources

- **PR:** #136 (MassTransit adapter)
- **Review:** data-integrity-guardian
- **Docs:** [MassTransit Error Handling](https://masstransit.io/documentation/configuration/middleware/error-handling)
- **Pattern:** [Dead Letter Queue Pattern](https://www.enterpriseintegrationpatterns.com/patterns/messaging/DeadLetterChannel.html)

## Acceptance Criteria

- [ ] Error queue configured with example in README
- [ ] Retry policy documented with code samples
- [ ] Integration test verifies retry behavior (5 attempts)
- [ ] Integration test verifies error queue routing
- [ ] No messages lost on handler exceptions
- [ ] Monitoring recommendations documented
- [ ] Code review confirms configuration

## Notes

- **Severity:** CRITICAL - production data loss risk
- **Impact:** Lost business transactions, no recovery path
- **Timeline:** MUST document before merging PR #136
- **Deployment:** Requires infrastructure (error queue) setup
- **Quote from review:** "Current implementation will cause data loss in production under normal failure scenarios (database timeouts, network issues, deployment restarts)."

## Work Log

### 2026-01-16 - Initial Discovery

**By:** Claude Code (data-integrity-guardian agent)

**Actions:**
- Identified missing DLQ configuration during data integrity review
- Analyzed message retry and failure paths
- Documented data loss scenarios
- Drafted configuration examples with retry policies

**Learnings:**
- MassTransit requires explicit error queue configuration
- Default behavior is message discard after retries
- Retry policies separate from error queue (both needed)
- Critical for production messaging reliability

### 2026-01-16 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
