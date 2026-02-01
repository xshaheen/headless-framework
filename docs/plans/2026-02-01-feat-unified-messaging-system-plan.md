# feat: Unify Headless.Ticker + Headless.Messaging into Single Messaging System

## Overview

Merge `Headless.Ticker` (background job scheduling with cron) and `Headless.Messaging` (distributed messaging with outbox) into a unified developer experience for **immediate messages**, **delayed messages**, **recurring scheduled jobs**, and **direct fire-and-forget publishing**.

**Target Packages:**
- `Headless.Messaging.Abstractions` → Core interfaces (`IConsume<T>`, `IPublisher`, `IDirectPublisher`)
- `Headless.Messaging.Core` → Unified runtime (dispatcher, scheduler, processors)
- `Headless.Messaging.Scheduling` → Cron/scheduling extensions (source generator preserved)
- `Headless.Messaging.Dashboard` → Consolidated dashboard (Ticker UI + Messaging data)
- Transport packages unchanged (RabbitMQ, Kafka, SQS, etc.)
- Storage packages unified (PostgreSQL, SqlServer, InMemory)

---

## Problem Statement

### Current Pain Points

| Issue | Ticker | Messaging | Impact |
|-------|--------|-----------|--------|
| **Two systems** | `AddTickerQ()` | `AddMessages()` | Cognitive overhead |
| **Different handlers** | `[TickerFunction]` attribute | `IConsume<T>` interface | Inconsistent patterns |
| **Separate storage** | `TimeTickerEntity`, `CronTickerEntity` | `Published`, `Received` tables | Duplicate persistence |
| **Separate dashboards** | `/tickerq/dashboard` | `/messaging` | Operational fragmentation |
| **No direct publish** | N/A | Everything goes through outbox | Overhead for fire-and-forget |
| **Overlapping concerns** | Retry, locking, monitoring | Retry, locking, monitoring | Duplicated code |

### Why Merge?

1. Developers shouldn't choose between "scheduling" and "messaging" - these are delivery timing mechanisms
2. Recurring job publishing a message = awkward cross-system coordination
3. Single transaction for job execution + message publishing
4. Unified monitoring and operational experience
5. Reduce package count (~8 Ticker + ~16 Messaging → ~18 total)

---

## Proposed Solution

### Unified API Design

```csharp
// ═══════════════════════════════════════════════════════════════════
// CONFIGURATION - Single entry point
// ═══════════════════════════════════════════════════════════════════

services.AddMessaging(m =>
{
    // Storage (required for outbox + scheduling)
    m.UsePostgreSql("connection_string");

    // Transport (required for broker-backed messaging)
    m.UseRabbitMQ(rabbit => rabbit.HostName = "localhost");

    // Consumer discovery
    m.ScanConsumers(typeof(Program).Assembly);

    // OR explicit registration with scheduling
    m.Consumer<DailyReportJob>()
        .WithSchedule("0 0 8 * * *")     // 8 AM daily (6-field cron)
        .WithTimeZone("America/New_York")
        .WithConcurrency(1)              // Singleton execution
        .Build();

    // Topic mappings for type-safe publishing
    m.WithTopicMapping<OrderPlaced>("orders.placed");

    // Global retry policy
    m.FailedRetryCount = 50;
    m.RetryBackoffStrategy = new ExponentialBackoffStrategy();

    // Enable direct publish (bypasses outbox)
    m.EnableDirectPublish();
});

// ═══════════════════════════════════════════════════════════════════
// IMMEDIATE MESSAGE HANDLER
// ═══════════════════════════════════════════════════════════════════

public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger)
    : IConsume<OrderPlaced>
{
    public async ValueTask Consume(
        ConsumeContext<OrderPlaced> context,
        CancellationToken ct)
    {
        logger.LogInformation("Processing order {OrderId}", context.Message.OrderId);
        // Business logic
    }
}

// ═══════════════════════════════════════════════════════════════════
// SCHEDULED/RECURRING JOB HANDLER
// ═══════════════════════════════════════════════════════════════════

// Option 1: Attribute-based (familiar to Ticker users)
[Recurring("0 */5 * * * *", Name = "CleanupExpiredTokens")]
public sealed class TokenCleanupJob(ITokenRepository tokens)
    : IConsume<ScheduledTrigger>
{
    public async ValueTask Consume(
        ConsumeContext<ScheduledTrigger> context,
        CancellationToken ct)
    {
        // context.Message contains: ScheduledTime, JobName, Attempt
        await tokens.DeleteExpiredAsync(ct);
    }
}

// Option 2: Fluent configuration (no attribute)
public sealed class DailyReportJob : IConsume<ScheduledTrigger> { ... }

// ═══════════════════════════════════════════════════════════════════
// PUBLISHING APIs
// ═══════════════════════════════════════════════════════════════════

public class OrderController(
    IOutboxPublisher outbox,      // Transactional (existing)
    IDirectPublisher direct)      // Fire-and-forget (NEW)
{
    // Outbox: Transactional publish (existing behavior)
    public async Task<IActionResult> CreateOrder(CreateOrderRequest req)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        outbox.Transaction = new OutboxTransaction(_db);

        var order = new Order(req);
        await _db.Orders.AddAsync(order);
        await outbox.PublishAsync(new OrderPlaced(order.Id));  // Same transaction

        await _db.SaveChangesAsync();
        await tx.CommitAsync();  // Message sent after commit
        return Ok();
    }

    // Direct: Fire-and-forget (NEW - bypasses outbox)
    public async Task<IActionResult> NotifyUser(NotifyRequest req)
    {
        // Single call - succeeds or throws immediately
        await direct.PublishAsync(new UserNotification(req.UserId, req.Message));
        return Ok();
    }

    // Delayed: Schedule for future (existing, enhanced)
    public async Task<IActionResult> ScheduleReminder(ReminderRequest req)
    {
        await outbox.PublishDelayAsync(
            TimeSpan.FromHours(24),
            new ReminderDue(req.UserId));
        return Ok();
    }
}
```

### Core Abstractions

```csharp
// ═══════════════════════════════════════════════════════════════════
// MESSAGE TYPES
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Marker message for scheduled/recurring job triggers.
/// Replaces TickerFunctionContext from Headless.Ticker.
/// </summary>
public sealed record ScheduledTrigger
{
    /// <summary>When the job was scheduled to run.</summary>
    public required DateTime ScheduledTime { get; init; }

    /// <summary>The job/handler name (from attribute or configuration).</summary>
    public required string JobName { get; init; }

    /// <summary>Current retry attempt (1-based).</summary>
    public required int Attempt { get; init; }

    /// <summary>The cron expression (for recurring jobs).</summary>
    public string? CronExpression { get; init; }

    /// <summary>Parent job ID for chained jobs.</summary>
    public Guid? ParentJobId { get; init; }

    /// <summary>Custom payload (optional, serialized).</summary>
    public string? Payload { get; init; }
}

// ═══════════════════════════════════════════════════════════════════
// DIRECT PUBLISHER (NEW)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Publishes messages directly to the transport, bypassing outbox storage.
/// Use for fire-and-forget scenarios where message loss is acceptable.
/// </summary>
public interface IDirectPublisher
{
    /// <summary>
    /// Publishes a message directly to the transport.
    /// Throws on failure - no retry, no storage.
    /// </summary>
    Task PublishAsync<T>(string topic, T message, CancellationToken ct = default)
        where T : class;

    /// <summary>
    /// Publishes using topic mapping.
    /// </summary>
    Task PublishAsync<T>(T message, CancellationToken ct = default)
        where T : class;

    /// <summary>
    /// Try-pattern for explicit failure handling.
    /// </summary>
    Task<DirectPublishResult> TryPublishAsync<T>(
        string topic,
        T message,
        CancellationToken ct = default) where T : class;
}

public readonly record struct DirectPublishResult(
    bool Success,
    Exception? Error = null,
    TimeSpan? Duration = null);

// ═══════════════════════════════════════════════════════════════════
// SCHEDULING ATTRIBUTE
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Marks an IConsume&lt;ScheduledTrigger&gt; handler as a recurring job.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RecurringAttribute : Attribute
{
    public RecurringAttribute(string cronExpression) => CronExpression = cronExpression;

    /// <summary>6-field cron expression (seconds, minutes, hours, day, month, weekday).</summary>
    public string CronExpression { get; }

    /// <summary>Job name for dashboard/logging. Defaults to class name.</summary>
    public string? Name { get; init; }

    /// <summary>IANA timezone. Defaults to UTC.</summary>
    public string? TimeZone { get; init; }

    /// <summary>Retry intervals in milliseconds for failed executions.</summary>
    public int[]? RetryIntervals { get; init; }

    /// <summary>Whether to skip if previous execution is still running.</summary>
    public bool SkipIfRunning { get; init; } = true;
}
```

---

## Technical Approach

### Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Headless.Messaging.Core                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐          │
│  │  IOutboxPublisher │  │  IDirectPublisher │  │  IScheduler       │          │
│  │  (transactional)  │  │  (fire-and-forget)│  │  (cron/delayed)   │          │
│  └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘          │
│           │                      │                      │                    │
│           ▼                      ▼                      ▼                    │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                        MessageDispatcher                                │ │
│  │  - Routes to IConsume<T> handlers                                       │ │
│  │  - Unified retry logic (IRetryBackoffStrategy)                          │ │
│  │  - OpenTelemetry instrumentation                                         │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                    │                                         │
│           ┌────────────────────────┼────────────────────────┐               │
│           ▼                        ▼                        ▼               │
│  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐         │
│  │ TransportSender │    │  OutboxStorage  │    │ SchedulerStorage│         │
│  │ (RabbitMQ/Kafka)│    │ (Published tbl) │    │ (Jobs table)    │         │
│  └─────────────────┘    └─────────────────┘    └─────────────────┘         │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘

Background Services:
┌────────────────────┐  ┌────────────────────┐  ┌────────────────────┐
│ OutboxProcessor    │  │ DelayedProcessor   │  │ SchedulerService   │
│ (sends pending)    │  │ (checks ExpiresAt) │  │ (cron polling)     │
└────────────────────┘  └────────────────────┘  └────────────────────┘
```

### Database Schema (Unified)

```sql
-- ═══════════════════════════════════════════════════════════════════
-- UNIFIED MESSAGES TABLE (replaces Published + Received)
-- ═══════════════════════════════════════════════════════════════════

CREATE TABLE messages (
    "Id" BIGINT PRIMARY KEY,
    "Version" VARCHAR(20) NOT NULL,
    "Type" VARCHAR(20) NOT NULL,           -- Published, Received, Scheduled
    "Name" VARCHAR(200) NOT NULL,          -- Topic name or Job name
    "Group" VARCHAR(200),                  -- Consumer group (Received only)
    "Content" TEXT NOT NULL,               -- Serialized message
    "Headers" JSONB,                       -- Message headers
    "Status" VARCHAR(50) NOT NULL,         -- Pending, Processing, Succeeded, Failed, Delayed
    "Retries" INT NOT NULL DEFAULT 0,
    "Added" TIMESTAMP NOT NULL,
    "ExpiresAt" TIMESTAMP,                 -- For delayed messages
    "ProcessedAt" TIMESTAMP,
    "MessageId" VARCHAR(200),              -- For deduplication

    INDEX idx_messages_status_type (Status, Type),
    INDEX idx_messages_expires (ExpiresAt) WHERE Status = 'Delayed'
);

-- ═══════════════════════════════════════════════════════════════════
-- SCHEDULED JOBS TABLE (replaces CronTickerEntity + TimeTickerEntity)
-- ═══════════════════════════════════════════════════════════════════

CREATE TABLE scheduled_jobs (
    "Id" UUID PRIMARY KEY,
    "Name" VARCHAR(200) NOT NULL,          -- Handler name
    "Type" VARCHAR(20) NOT NULL,           -- Recurring, OneTime
    "CronExpression" VARCHAR(100),         -- For recurring jobs
    "TimeZone" VARCHAR(100) DEFAULT 'UTC',
    "Payload" TEXT,                        -- Optional custom payload
    "Status" VARCHAR(50) NOT NULL,         -- Pending, Running, Completed, Failed, Disabled
    "NextRunTime" TIMESTAMP,               -- Next scheduled execution
    "LastRunTime" TIMESTAMP,
    "LastRunDuration" BIGINT,              -- Milliseconds
    "RetryCount" INT NOT NULL DEFAULT 0,
    "RetryIntervals" INT[],                -- Milliseconds per retry
    "LockHolder" VARCHAR(256),             -- Instance holding execution lock
    "LockedAt" TIMESTAMP,
    "CreatedAt" TIMESTAMP NOT NULL,
    "UpdatedAt" TIMESTAMP NOT NULL,
    "IsEnabled" BOOLEAN DEFAULT TRUE,

    INDEX idx_jobs_next_run (NextRunTime) WHERE Status = 'Pending' AND IsEnabled = TRUE,
    INDEX idx_jobs_lock (LockHolder, LockedAt) WHERE Status = 'Running'
);

-- ═══════════════════════════════════════════════════════════════════
-- JOB EXECUTIONS TABLE (replaces CronTickerOccurrenceEntity)
-- ═══════════════════════════════════════════════════════════════════

CREATE TABLE job_executions (
    "Id" UUID PRIMARY KEY,
    "JobId" UUID NOT NULL REFERENCES scheduled_jobs(Id) ON DELETE CASCADE,
    "ScheduledTime" TIMESTAMP NOT NULL,
    "StartedAt" TIMESTAMP,
    "CompletedAt" TIMESTAMP,
    "Status" VARCHAR(50) NOT NULL,         -- Pending, Running, Succeeded, Failed
    "Duration" BIGINT,                     -- Milliseconds
    "RetryAttempt" INT NOT NULL DEFAULT 1,
    "Error" TEXT,                          -- Error message if failed
    "Output" TEXT,                         -- Optional execution output

    INDEX idx_executions_job_status (JobId, Status),
    INDEX idx_executions_scheduled (ScheduledTime DESC)
);

-- ═══════════════════════════════════════════════════════════════════
-- DISTRIBUTED LOCK TABLE (shared)
-- ═══════════════════════════════════════════════════════════════════

CREATE TABLE distributed_locks (
    "Key" VARCHAR(256) PRIMARY KEY,
    "Instance" VARCHAR(256) NOT NULL,
    "AcquiredAt" TIMESTAMP NOT NULL,
    "ExpiresAt" TIMESTAMP NOT NULL,

    INDEX idx_locks_expires (ExpiresAt)
);
```

---

## Implementation Phases

### Phase 1: Core Abstractions & Direct Publisher

| # | Story | Size | Depends On |
|---|-------|------|------------|
| 1.1 | Create `ScheduledTrigger` message type in Abstractions | S | - |
| 1.2 | Create `IDirectPublisher` interface in Abstractions | S | - |
| 1.3 | Create `RecurringAttribute` in Abstractions | S | 1.1 |
| 1.4 | Create `DirectPublishResult` and related types | XS | 1.2 |
| 1.5 | Implement `DirectPublisher` in Core (RabbitMQ first) | M | 1.2 |
| 1.6 | Add `EnableDirectPublish()` to `MessagingOptions` | S | 1.5 |
| 1.7 | Add OpenTelemetry instrumentation for direct publish | S | 1.5 |
| 1.8 | Write unit tests for `DirectPublisher` | M | 1.5 |
| 1.9 | Write integration tests with RabbitMQ transport | M | 1.5 |

**Success criteria:** `IDirectPublisher` works with RabbitMQ, bypasses outbox, throws on failure.

**Phase total:** ~16hr

---

### Phase 2: Scheduling Infrastructure

| # | Story | Size | Depends On |
|---|-------|------|------------|
| 2.1 | Create unified `scheduled_jobs` schema in PostgreSql provider | M | - |
| 2.2 | Create unified `job_executions` schema | S | 2.1 |
| 2.3 | Create `IScheduledJobStorage` abstraction | M | - |
| 2.4 | Implement PostgreSql `ScheduledJobStorage` | L | 2.1, 2.3 |
| 2.5 | Port `CronScheduleCache` from Ticker (cron parsing) | S | - |
| 2.6 | Create `SchedulerBackgroundService` (polls for due jobs) | L | 2.3, 2.5 |
| 2.7 | Create `IScheduledJobManager` for CRUD operations | M | 2.3 |
| 2.8 | Integrate scheduler with `MessageDispatcher` | M | 2.6, 1.1 |
| 2.9 | Add distributed locking for job execution | M | 2.6 |
| 2.10 | Write unit tests for scheduler | L | 2.6 |
| 2.11 | Write integration tests with PostgreSQL | M | 2.4, 2.6 |

**Success criteria:** Cron jobs can be registered, scheduled, and executed via `IConsume<ScheduledTrigger>`.

**Phase total:** ~32hr

---

### Phase 3: Source Generator Enhancement

| # | Story | Size | Depends On |
|---|-------|------|------------|
| 3.1 | Analyze existing Ticker source generator | S | - |
| 3.2 | Extend generator to scan `[Recurring]` on `IConsume<ScheduledTrigger>` | L | 3.1 |
| 3.3 | Generate job registration code at compile time | M | 3.2 |
| 3.4 | Support `[FromKeyedServices]` in generated constructors | S | 3.2 |
| 3.5 | Add analyzer for invalid `[Recurring]` usage | M | 3.2 |
| 3.6 | Write generator tests | M | 3.2 |
| 3.7 | Update documentation with generator usage | S | 3.2 |

**Success criteria:** `[Recurring]` attribute on `IConsume<ScheduledTrigger>` auto-registers jobs at compile time.

**Phase total:** ~18hr

---

### Phase 4: Dashboard Consolidation

| # | Story | Size | Depends On |
|---|-------|------|------------|
| 4.1 | Design unified dashboard wireframes | S | - |
| 4.2 | Merge Ticker dashboard UI components into Messaging.Dashboard | L | 4.1 |
| 4.3 | Create unified `/api/jobs` endpoints | M | Phase 2 |
| 4.4 | Create unified `/api/messages` endpoints | M | - |
| 4.5 | Create unified `/api/stats` aggregating jobs + messages | M | 4.3, 4.4 |
| 4.6 | Add SignalR hub for real-time job status updates | M | 4.3 |
| 4.7 | Implement job enable/disable/trigger from dashboard | S | 4.3 |
| 4.8 | Add authentication modes (Basic, ApiKey, Host) | M | 4.2 |
| 4.9 | Write E2E tests for dashboard | M | 4.2 |

**Success criteria:** Single dashboard at `/messaging` shows both scheduled jobs and messages with real-time updates.

**Phase total:** ~24hr

---

### Phase 5: Direct Publisher for All Transports

| # | Story | Size | Depends On |
|---|-------|------|------------|
| 5.1 | Implement `DirectPublisher` for Kafka | M | Phase 1 |
| 5.2 | Implement `DirectPublisher` for AWS SQS | M | Phase 1 |
| 5.3 | Implement `DirectPublisher` for Azure Service Bus | M | Phase 1 |
| 5.4 | Implement `DirectPublisher` for NATS | S | Phase 1 |
| 5.5 | Implement `DirectPublisher` for Redis Streams | S | Phase 1 |
| 5.6 | Implement `DirectPublisher` for Pulsar | M | Phase 1 |
| 5.7 | Integration tests for each transport | L | 5.1-5.6 |

**Success criteria:** `IDirectPublisher` works with all supported transports.

**Phase total:** ~20hr

---

### Phase 6: Cleanup & Migration

| # | Story | Size | Depends On |
|---|-------|------|------------|
| 6.1 | Mark `Headless.Ticker.*` packages as deprecated | S | All phases |
| 6.2 | Create migration guide documentation | M | All phases |
| 6.3 | Create adapter for `[TickerFunction]` → `IConsume<ScheduledTrigger>` | M | Phase 2-3 |
| 6.4 | Remove Ticker source code from main branch | S | 6.1, 6.3 |
| 6.5 | Update all sample projects | M | All phases |
| 6.6 | Update README files for all affected packages | M | All phases |

**Success criteria:** Ticker packages deprecated, migration path clear, unified system is default.

**Phase total:** ~12hr

---

## Sizing Summary

| Size | Count | Est. Hours |
|------|-------|------------|
| XS | 2 | 1 |
| S | 16 | 24 |
| M | 22 | 66 |
| L | 6 | 36 |
| **Total** | **46 stories** | **~127hr** |

---

## Acceptance Criteria

### Functional Requirements

- [ ] [M] `IConsume<ScheduledTrigger>` handles cron jobs via `[Recurring]` attribute
- [ ] [M] `IConsume<T>` handles immediate messages (existing behavior preserved)
- [ ] [M] `IDirectPublisher.PublishAsync()` sends to transport, throws on failure, no outbox
- [ ] [M] `IOutboxPublisher.PublishDelayAsync()` schedules messages for future delivery
- [ ] [S] Cron expressions support 6-field format with timezone configuration
- [ ] [S] Distributed locking prevents concurrent job execution on same schedule
- [ ] [S] Dashboard shows unified view of jobs + messages at `/messaging`
- [ ] [S] Dashboard supports real-time updates via SignalR

### Non-Functional Requirements

- [ ] [M] Direct publish adds < 5ms latency vs direct transport call
- [ ] [S] Scheduler polling interval configurable (default 1s)
- [ ] [S] Job execution timeout configurable per-job
- [ ] [XS] No breaking changes to existing `IConsume<T>` handlers

### Quality Gates

- [ ] [M] ≥85% line coverage, ≥80% branch coverage for new code
- [ ] [S] All public APIs have XML documentation
- [ ] [S] Integration tests for PostgreSQL + RabbitMQ combination
- [ ] [S] Integration tests for SqlServer + Azure Service Bus combination

---

## Alternative Approaches Considered

### 1. Keep Ticker and Messaging Separate

**Rejected because:**
- Ongoing maintenance burden for overlapping functionality
- Developer confusion about which system to use
- No unified operational experience

### 2. Merge Everything into One Package

**Rejected because:**
- Would force all users to take scheduling dependencies
- Larger package size for simple messaging scenarios
- Less flexible opt-in model

### 3. Use Hangfire/Quartz.NET Instead

**Rejected because:**
- Different architectural philosophy (pull vs push)
- Loss of control over storage schema
- Dependency on third-party release cycle
- Existing investment in Ticker source generator

---

## Risk Analysis & Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Breaking existing Messaging users | High | Low | Keep `IConsume<T>` signature unchanged |
| Performance regression in scheduler | Medium | Medium | Benchmark against Ticker baseline |
| Source generator complexity | Medium | Medium | Incremental extraction from existing Ticker generator |
| Dashboard UI merge conflicts | Low | High | Design unified component library first |

---

## Dependencies & Prerequisites

- [ ] .NET 10 SDK
- [ ] PostgreSQL 14+ for integration tests
- [ ] RabbitMQ 3.12+ for integration tests
- [ ] Docker for Testcontainers

---

## ERD Diagram

```
┌──────────────────────┐       ┌──────────────────────┐
│      messages        │       │   scheduled_jobs     │
├──────────────────────┤       ├──────────────────────┤
│ Id (PK)              │       │ Id (PK)              │
│ Type                 │       │ Name                 │
│ Name                 │       │ Type                 │
│ Group                │       │ CronExpression       │
│ Content              │       │ TimeZone             │
│ Headers              │       │ Payload              │
│ Status               │       │ Status               │
│ Retries              │       │ NextRunTime          │
│ Added                │       │ LastRunTime          │
│ ExpiresAt            │       │ LockHolder           │
│ ProcessedAt          │       │ IsEnabled            │
│ MessageId            │       │ CreatedAt            │
└──────────────────────┘       │ UpdatedAt            │
                               └──────────┬───────────┘
                                          │
                                          │ 1:N
                                          ▼
                               ┌──────────────────────┐
                               │   job_executions     │
                               ├──────────────────────┤
                               │ Id (PK)              │
                               │ JobId (FK)           │
                               │ ScheduledTime        │
                               │ StartedAt            │
                               │ CompletedAt          │
                               │ Status               │
                               │ Duration             │
                               │ RetryAttempt         │
                               │ Error                │
                               └──────────────────────┘

┌──────────────────────┐
│  distributed_locks   │
├──────────────────────┤
│ Key (PK)             │
│ Instance             │
│ AcquiredAt           │
│ ExpiresAt            │
└──────────────────────┘
```

---

## References & Research

### Internal References

- Ticker abstractions: `src/Headless.Ticker.Abstractions/`
- Ticker source generator: `src/Headless.Ticker.SourceGenerator/`
- Messaging core: `src/Headless.Messaging.Core/Setup.cs`
- IConsume interface: `src/Headless.Messaging.Abstractions/IConsume.cs`
- IOutboxPublisher: `src/Headless.Messaging.Abstractions/IOutboxPublisher.cs`
- Ticker dashboard: `src/Headless.Ticker.Dashboard/`
- Messaging dashboard: `src/Headless.Messaging.Dashboard/`

### External References

- CAP (similar .NET messaging library): https://cap.dotnetcore.xyz/
- MassTransit job consumers: https://masstransit.io/documentation/patterns/job-consumers
- Hangfire recurring jobs: https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html

---

## Unresolved Questions

1. **Cron timezone handling in DST transitions** - Should we log warnings or silently handle?

2. **Direct publish with multiple transports** - When multiple transports are configured, which one receives the direct publish? Default to first? Require explicit selection?

3. **Job chaining** - Ticker supports `ParentId` for chained jobs. Should unified system support this, or defer to explicit message publishing between jobs?

4. **Rate limiting for direct publish** - Should `IDirectPublisher` have built-in rate limiting, or leave to user?

5. **Payload size limits** - Should `ScheduledTrigger.Payload` have a max size, or rely on storage limits?
