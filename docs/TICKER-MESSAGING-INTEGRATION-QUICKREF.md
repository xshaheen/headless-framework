# Ticker + Messaging Integration: Quick Reference

**For developers starting integration work. Read this before reading the full institutional learnings doc.**

---

## 🚨 Critical Gotchas

### 1. Message Ordering Depends on Transport
```
Kafka      → Strict per-partition (use partition key!)
Azure SB   → FIFO only if sessions enabled (not default)
RabbitMQ   → NO ordering by default
SQS FIFO   → Strict (but standard SQS has none)
```
**Action**: If Ticker→Messaging, choose transport and test ordering.

### 2. ConsumerThreadCount Breaks Message Order
```csharp
options.ConsumerThreadCount > 1  // ← Messages process out of order!
```
**Action**: For order-sensitive Ticker→Message flows, use `ConsumerThreadCount = 1`.

### 3. Distributed Locks Are Required
Both systems need locks for instance coordination:
- **Ticker**: "Only instance A should process this job"
- **Messaging**: "Only instance B should consume this message"

**Current**: Use `IResourceLockProvider` (separate package)
**Gotcha**: Lock timeout → other instances also try → duplicate work
**Action**: Set realistic timeouts; monitor lock acquisition failures.

### 4. Cascade Deletes Are Fragile
Ticker deletes parent job → what about published messages?
```
Job deleted → message still in queue → consumer crashes
```
**Action**: Test cleanup scenarios; consider soft deletes or message versioning.

### 5. Timezone Matters for Cron
```csharp
[TickerQ("0 9 * * MON")] // What timezone? UTC? Local? EST?
```
**Action**: Always configure `TimeZoneInfo` explicitly; document in code comments.

---

## ✅ Proven Patterns (Already in Codebase)

### Pattern 1: Outbox for Transactional Publishing
```csharp
using (var tx = transaction.Begin())
{
    await db.SaveChangesAsync(ct);
    await outbox.PublishAsync("topic", message, ct);  // Atomic!
    await tx.CommitAsync(ct);
}
```
**Use case**: Ticker job completes → publish result message atomically.

### Pattern 2: Abstraction + Provider Separation
```
Headless.Messaging.Abstractions     ← Interfaces only
Headless.Messaging.Core             ← Default impl
Headless.Messaging.RabbitMq         ← Provider impl
```
**Lesson**: Keep Ticker + Messaging separate at abstraction level; compose in Core.

### Pattern 3: InMemory Provider for Testing
```csharp
// Unit tests
options.UseInMemoryMessaging();
options.UseInMemoryTicker();

// Integration tests
options.UsePostgreSql(...);  // Real database
```
**Benefit**: Fast unit tests; realistic integration tests.

### Pattern 4: Distributed Coordination via Locks
```csharp
var @lock = await lockProvider.TryAcquireAsync(
    "process:job:123",
    timeUntilExpires: TimeSpan.FromMinutes(5),
    acquireTimeout: TimeSpan.FromSeconds(30),
    ct
);
if (@lock is null) throw new ConcurrencyException("Could not acquire");
using (await @lock)
{
    // Only this instance executes here
}
```

---

## 🏗️ Architecture Guidelines

### Separate, Don't Merge
Keep Ticker and Messaging as separate systems:
- ✅ Compose them (`UseTicker()` + `UseMessaging()`)
- ❌ Don't inherit from each other
- ❌ Don't share persistence providers
- ✅ Share abstractions via `Headless.*.Abstractions`

### Unified Dashboards (Optional)
Both have separate dashboards for a reason:
- `Headless.Messaging.Dashboard`
- `Headless.Ticker.Dashboard`

**Short-term**: Keep separate
**Long-term**: Could unify if tracing correlates Ticker→Message flows

### Single IOutboxPublisher
```csharp
public sealed class TickerJobCompletionHandler(IOutboxPublisher outbox)
{
    public async Task OnJobCompleted(TickerId jobId, CancellationToken ct)
    {
        await outbox.PublishAsync("ticker.completed", new JobCompletedEvent { JobId = jobId }, ct);
    }
}
```
**Benefit**: Centralized message source; easier to trace.

---

## 🧪 Testing Strategy

### Unit Tests: Use InMemory Providers
```csharp
public class ScheduledJobPublishesMessageTests(ITestContext ctx) : TestBase
{
    [Fact]
    public async Task should_publish_message_when_job_completes()
    {
        // Given
        var storage = new InMemoryMessageStorage();
        var ticker = new InMemoryTickerPersistenceProvider();
        // ...

        // When
        await handler.ExecuteAsync(job, AbortToken);

        // Then
        var published = await storage.GetPublishedAsync(AbortToken);
        published.Should().Contain(x => x.Topic == "job.completed");
    }
}
```

### Integration Tests: Use Real Dependencies + Testcontainers
```csharp
public class ScheduledJobToMessageIntegrationTests(PostgresFixture postgres) : TestBase
{
    [Fact]
    public async Task should_end_to_end_schedule_job_and_consume_message()
    {
        // Uses real DB, real Transport (RabbitMQ/Kafka), real Ticker
        // Tests actual ordering, delivery semantics, etc.
    }
}
```

### Key Test Scenarios
- [ ] Scheduled job publishes message (happy path)
- [ ] Job retry doesn't duplicate messages (idempotency)
- [ ] Message ordering preserved (if transport supports)
- [ ] Lock timeouts handled gracefully
- [ ] Graceful shutdown (job in-flight, message undelivered)
- [ ] Cascade deletes don't orphan messages
- [ ] Consumer failover (instance dies, another picks up)

---

## 📋 Integration Checklist

### Before You Start
- [ ] Read full `INSTITUTIONAL-LEARNINGS-TICKER-MESSAGING-INTEGRATION.md`
- [ ] Review test designs: `/docs/test-designs/Headless.Ticker.TestDesign.md`
- [ ] Review messaging harness plan: `/docs/plans/2026-02-01-feat-messaging-test-harness-plan.md`

### During Implementation
- [ ] Use primary constructors (DI)
- [ ] Use `Argument.IsNotNull()` for validation
- [ ] Use `AbortToken` in tests (not `CancellationToken`)
- [ ] Propagate `CancellationToken` through all async chains
- [ ] Use `IResourceLockProvider` for distributed coordination
- [ ] Document timezone config for cron jobs
- [ ] Test message ordering if order-sensitive

### Before Merge to Main
- [ ] All tests pass locally + CI
- [ ] Documentation updated (READMEs, code comments)
- [ ] Integration test harness created
- [ ] Gotchas documented for consumers
- [ ] Performance baseline established

---

## 🔍 Where to Find Things

| Need | Location |
|------|----------|
| Outbox pattern | `/src/Headless.Messaging.Abstractions/` |
| Distributed locks | `/src/Headless.DistributedLocks.Core/` |
| Ticker scheduler | `/src/Headless.Ticker.Core/` |
| Messaging consumer | `/src/Headless.Messaging.Core/` |
| Test harness example | `/tests/Headless.Messaging.Core.Tests.Harness/` |
| Test designs | `/docs/test-designs/Headless.Ticker.TestDesign.md` |
| Code style rules | `/CLAUDE.md` |

---

## 🎯 Quick Decision Matrix

### "Should I publish via Outbox or Direct Message?"
```
Job completes in same DB transaction as state change?
  YES → Use Outbox (atomic, no race conditions)
  NO  → Use Direct IMessagePublisher (simpler, eventual consistency)
```

### "What Transport Should I Choose?"
```
Need strict message ordering?
  YES → Use Kafka (partition key) or Azure SB (sessions) or SQS FIFO
  NO  → Use RabbitMQ or Redis Streams (simpler)

High throughput, eventual consistency OK?
  YES → Use Kafka or Pulsar with parallel consumers
  NO  → Use RabbitMQ or SQS with single consumer

Running in Kubernetes?
  YES → Consider Azure SB or Pulsar (cloud-native)
  NO  → RabbitMQ or Kafka (self-hosted)
```

### "Should Jobs Be Serialized in Message?"
```
Message < 1KB?
  YES → Serialize job details, defer to message consumer
  NO  → Store in DB, pass JobId in message (consumer fetches full details)
```

---

## 📞 Common Questions

**Q: Can a Ticker job publish to the same topic it consumes from?**
A: Possible but risky. Avoid if possible; test idempotency if unavoidable.

**Q: What if Ticker scheduling hangs?**
A: All other jobs in that instance hang. Use timeouts on long-running operations.

**Q: Can I reuse IOutboxPublisher in Ticker tasks?**
A: Yes! But wrap in try-catch; failed publishes shouldn't block job execution.

**Q: How do I trace a scheduled job through to message delivery?**
A: Add CorrelationId to both job and message; implement tracing decorator on both.

**Q: Should I version Ticker job requests?**
A: Yes, if job handler might change. Include version in serialized request.

---

## 🚀 Next Steps

1. **Review gotchas** (above) - discuss with team
2. **Read full doc** - deep context for design decisions
3. **Create integration test** - "job publishes message" scenario
4. **Extend harness** - add Ticker+Messaging base class to test infrastructure
5. **Document examples** - show consumers how to use both systems together

---

**Created**: 2026-02-01
**Scope**: Integration planning & quick reference
**Audience**: Developers implementing Ticker ↔ Messaging features
