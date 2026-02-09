# feat: Execution Graph — Job/Message Hierarchy Tracking

> **Scope:** Design for tracking parent-child relationships between scheduled jobs, job→message causation chains, and fan-out/fan-in patterns across both scheduling and messaging subsystems.
>
> **Not in scope:** Dashboard/UI for graph visualization (future), OpenTelemetry Activity integration (Full Merge US-032–034), saga/state machine orchestration.

## Overview

The scheduling and messaging subsystems currently have no concept of hierarchy. Jobs are flat — no job can declare another as its parent, no execution can reference what spawned it, and messages published from job handlers carry only a flat `CorrelationId` with no causation chain. This plan introduces a layered execution graph that enables:

1. **Job chaining** — Job A completes → Job B fires (with configurable run conditions)
2. **Fan-out/fan-in** — Job A spawns N child jobs → all complete → continuation fires
3. **Correlation tracing** — Full causation chain from root trigger through all descendant jobs and messages

---

## Research Summary

### What the Industry Does

| System | Hierarchy Model | Storage | Key Insight |
|--------|----------------|---------|-------------|
| **Hangfire** | Dual-pointer: parent stores `Continuations` JobParameter, child stores `ParentId` in AwaitingState.Data | No schema column — metadata in polymorphic state JSON | Lightweight; no FK; but fragile to query |
| **MassTransit** | 3-ID causation: `ConversationId` (root), `InitiatorId` (parent MessageId), `CorrelationId` (business) | Message headers only — no persistent store | Purely header-based; automatic propagation; no fan-in without Sagas |
| **Temporal** | Event-sourced execution trees: `parentWorkflowExecution` + `rootWorkflowExecution` on child | Event history per workflow; parent/root pointers on ChildWorkflowExecutionStarted event | Most powerful but heaviest; event sourcing overkill for us |
| **Machinery** | Linked-list chains (`OnSuccess`/`OnError`), `GroupMeta` with chord callback for fan-in | In-memory task graph; broker-backed results | Simple primitives compose well; chord = "when all done, call this" |
| **River (Go)** | DAG via metadata-in-job-row JSONB, named string dependencies, `Prepare()` validation | `metadata JSONB` column on job row; `InsertManyTx` for atomic batch | Elegant — no extra tables; deps as JSON array in existing row |
| **TickerQ (removed)** | Self-referential `ParentId` FK on entity, `RunCondition` enum (6 conditions), `FluentChainTickerBuilder` (850 lines) | Dedicated FK column + index; ConcurrentDictionary parent index at runtime | Over-engineered for our needs; max depth 3, max fan-out 5 limits |

### Current State (Headless.Messaging)

- **`ScheduledJob`** — 21 properties, completely flat. No `ParentJobId`, `ParentJobName`, or `RootJobId`.
- **`JobExecution`** — 8 properties. No `ParentExecutionId`, `CorrelationId`, or `SpawnedBy`.
- **`ScheduledTrigger`** — Has `ParentJobId` (Guid?) but always set to `null` in dispatcher (line 83). Placeholder.
- **`MessagingCorrelationScope`** — AsyncLocal with `CorrelationId` + sequence counter. Flat — no `CausationId` or `RootCorrelationId`.
- **`Headers`** — `headless-corr-id`, `headless-corr-seq`. No `headless-causation-id`, `headless-root-corr-id`, or `headless-parent-job-id`.
- **`ConsumeContext`** — `CorrelationId` (flat), no `CausationId` or `InitiatorId`.

### Prior Architectural Decision

From `docs/plans/2026-02-07-feat-job-execution-correlation-plan.md`:

> "Ticker's model is about job→job parent-child execution trees. We don't need execution trees — the unified messaging system provides better composition via message publishing from handlers."

This was correct for the correlation-only scope. The execution graph plan builds on top of that foundation — correlation propagation (US-006–009) must land first.

---

## Design Approach

### Principles

1. **Metadata-in-row, not new tables** — Like River, store hierarchy pointers as nullable columns on existing entities. No `job_dependencies` junction table.
2. **Headers carry causation, entities carry hierarchy** — Messages get `CausationId`/`RootCorrelationId` headers. Jobs/executions get `ParentJobId`/`RootJobId` columns.
3. **Opt-in complexity** — Chaining and fan-in are additive. Jobs without parents work exactly as today.
4. **No FluentBuilder** — TickerQ's 850-line `FluentChainTickerBuilder` was over-engineered. Use `IScheduledJobManager` extensions instead.
5. **Depth/fan-out limits** — Prevent runaway graphs. Configurable but defaulted (max depth 5, max fan-out 10).

### Approach Comparison

| Aspect | Option A: Scheduling-Only | Option B: Unified (Messaging + Scheduling) | **Chosen** |
|--------|--------------------------|---------------------------------------------|------------|
| **Job chaining** | `ParentJobId` on `ScheduledJob` + `RunCondition` | Same | B |
| **Fan-in** | Poll child execution status | Same + message-based completion signals | B |
| **Message causation** | Not addressed | `CausationId` + `RootCorrelationId` headers | B |
| **Cross-subsystem tracing** | Broken at job→message boundary | End-to-end: job → message → child job | B |
| **Complexity** | Lower | Moderate | Acceptable — the value is in cross-subsystem tracing |

**Chosen: Option B (Unified)** — The primary value of hierarchy tracking is answering "what caused this?" across the full system, not just within scheduling.

---

## Technical Design

### Layer 1: Causation Headers (Messaging)

Extend `MessagingCorrelationScope` and `Headers` with causation chain support.

**New headers:**

| Header | Constant | Meaning |
|--------|----------|---------|
| `headless-causation-id` | `Headers.CausationId` | MessageId of the message that directly caused this one |
| `headless-root-corr-id` | `Headers.RootCorrelationId` | CorrelationId of the root trigger (propagated unchanged through entire tree) |

**`MessagingCorrelationScope` additions:**

```csharp
public sealed class MessagingCorrelationScope : IDisposable
{
    // Existing
    public string CorrelationId { get; }

    // New
    public string? CausationId { get; }
    public string RootCorrelationId { get; }

    public static MessagingCorrelationScope Begin(
        string correlationId,
        string? causationId = null,
        string? rootCorrelationId = null,
        int initialSequence = 0)
    {
        // rootCorrelationId defaults to correlationId when null (this is the root)
    }
}
```

**Propagation rules:**

| Publisher scenario | `CorrelationId` | `CausationId` | `RootCorrelationId` |
|-------------------|-----------------|---------------|---------------------|
| No ambient scope | `MessageId` (existing) | `null` | `null` |
| Within scope (job handler) | `scope.CorrelationId` | `scope.CorrelationId` | `scope.RootCorrelationId` |
| Within scope (message handler) | `scope.CorrelationId` | consumed message's `MessageId` | `scope.RootCorrelationId` |
| Explicit headers | Caller's value wins | Caller's value wins | Caller's value wins |

### Layer 2: Job Hierarchy (Scheduling)

**`ScheduledJob` additions:**

```csharp
public sealed class ScheduledJob
{
    // Existing 21 properties...

    /// <summary>
    /// Gets or sets the identifier of the parent job that this job depends on.
    /// </summary>
    public Guid? ParentJobId { get; set; }

    /// <summary>
    /// Gets or sets the run condition determining when this child job fires
    /// relative to its parent's execution outcome.
    /// </summary>
    public JobRunCondition RunCondition { get; set; }
}
```

**`JobRunCondition` enum:**

```csharp
/// <summary>
/// Defines when a child job should run relative to its parent's execution outcome.
/// </summary>
public enum JobRunCondition
{
    /// <summary>No parent dependency — runs on schedule (default).</summary>
    None = 0,

    /// <summary>Runs after parent completes successfully.</summary>
    OnParentSuccess = 1,

    /// <summary>Runs after parent fails (all retries exhausted).</summary>
    OnParentFailure = 2,

    /// <summary>Runs after parent completes regardless of outcome.</summary>
    OnParentCompletion = 3,
}
```

**Design notes:**
- `RunCondition.None` is the default — existing jobs are unaffected.
- Unlike TickerQ's 6 conditions (`Always`, `OnSuccess`, `OnFailure`, `OnTimeout`, `OnCancelled`, `Custom`), we use 3+None. `OnTimeout` and `OnCancelled` are edge cases of `OnParentFailure` since both produce a non-success status. `Custom` is YAGNI.
- No `MaxDepth`/`MaxFanOut` columns on the entity. Enforce at the API/manager level via `SchedulingOptions`.

**`JobExecution` additions:**

```csharp
public sealed class JobExecution
{
    // Existing 8 properties...

    /// <summary>
    /// Gets or sets the identifier of the parent execution that triggered this one.
    /// </summary>
    public Guid? ParentExecutionId { get; set; }

    /// <summary>
    /// Gets or sets the root execution identifier for the execution tree.
    /// </summary>
    public Guid? RootExecutionId { get; set; }
}
```

### Layer 3: Child Job Triggering

When a parent job execution completes, the engine checks for child jobs:

```
ScheduledJobExecutor.ExecuteAsync():
  1. Run job via ScheduledJobDispatcher
  2. Record execution outcome (Success/Failed)
  3. Query child jobs: SELECT * FROM scheduled_jobs WHERE parent_job_id = @jobId
  4. For each child job matching RunCondition:
     a. Set NextRunTime = now (so scheduler picks it up next cycle)
     b. Create execution with ParentExecutionId = parent.execution.Id, RootExecutionId = parent.RootExecutionId ?? parent.execution.Id
```

**Fan-in detection:**
- A job with `ParentJobId` set and multiple sibling jobs forms a fan-in group.
- The engine checks: "Are all siblings of this job's parent complete?" before triggering a designated continuation job.
- Implementation: `IScheduledJobStorage.GetChildJobsAsync(parentJobId)` + check all executions.

### Layer 4: `IScheduledJobManager` Extensions

```csharp
public interface IScheduledJobManager
{
    // Existing methods...

    /// <summary>
    /// Schedules a one-time child job that runs after the specified parent job's next execution
    /// completes with the given condition.
    /// </summary>
    Task ScheduleChildAsync(
        string name,
        string parentJobName,
        Type consumerType,
        JobRunCondition runCondition = JobRunCondition.OnParentSuccess,
        string? payload = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the execution tree for a given root execution.
    /// </summary>
    Task<IReadOnlyList<JobExecution>> GetExecutionTreeAsync(
        Guid rootExecutionId,
        CancellationToken cancellationToken = default);
}
```

### Layer 5: `ScheduledTrigger` Update

Replace the always-null `ParentJobId` with meaningful hierarchy data:

```csharp
public sealed record ScheduledTrigger
{
    // Existing
    public required DateTimeOffset ScheduledTime { get; init; }
    public required string JobName { get; init; }
    public required int Attempt { get; init; }
    public string? CronExpression { get; init; }
    public string? Payload { get; init; }

    // Updated (was always null, now populated for child jobs)
    public Guid? ParentJobId { get; init; }

    // New
    public Guid? ParentExecutionId { get; init; }
    public Guid? RootExecutionId { get; init; }
}
```

### Configuration

```csharp
public sealed class SchedulingOptions
{
    // Existing options...

    /// <summary>
    /// Maximum depth of job hierarchy (root = depth 0). Default: 5.
    /// </summary>
    public int MaxJobDepth { get; set; } = 5;

    /// <summary>
    /// Maximum number of direct child jobs per parent. Default: 10.
    /// </summary>
    public int MaxFanOut { get; set; } = 10;
}
```

### Storage Changes

**`IScheduledJobStorage` additions:**

```csharp
public interface IScheduledJobStorage
{
    // Existing 11 methods...

    /// <summary>
    /// Gets all child jobs of a given parent job.
    /// </summary>
    Task<IReadOnlyList<ScheduledJob>> GetChildJobsAsync(
        Guid parentJobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets executions forming an execution tree from a root execution.
    /// </summary>
    Task<IReadOnlyList<JobExecution>> GetExecutionTreeAsync(
        Guid rootExecutionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the depth from a job to the root of its hierarchy.
    /// </summary>
    Task<int> GetJobDepthAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
```

**PostgreSQL migration (for `ScheduledJobStorage`):**

```sql
-- ScheduledJob additions
ALTER TABLE scheduled_jobs ADD COLUMN parent_job_id UUID REFERENCES scheduled_jobs(id) ON DELETE SET NULL;
ALTER TABLE scheduled_jobs ADD COLUMN run_condition INTEGER NOT NULL DEFAULT 0;
CREATE INDEX ix_scheduled_jobs_parent_job_id ON scheduled_jobs(parent_job_id) WHERE parent_job_id IS NOT NULL;

-- JobExecution additions
ALTER TABLE job_executions ADD COLUMN parent_execution_id UUID REFERENCES job_executions(id) ON DELETE SET NULL;
ALTER TABLE job_executions ADD COLUMN root_execution_id UUID REFERENCES job_executions(id) ON DELETE SET NULL;
CREATE INDEX ix_job_executions_parent_execution_id ON job_executions(parent_execution_id) WHERE parent_execution_id IS NOT NULL;
CREATE INDEX ix_job_executions_root_execution_id ON job_executions(root_execution_id) WHERE root_execution_id IS NOT NULL;
```

**InMemory provider:** Add `LINQ` filtering on new properties. No schema changes needed.

---

## Dependency Chain

```
US-006–009 (Correlation Propagation) — MUST land first
    │
    ├── EG-001: Causation headers (extends correlation scope)
    │
    ├── EG-002: Job hierarchy entities (ParentJobId, RunCondition)
    │   │
    │   ├── EG-003: Storage layer (new columns + queries)
    │   │   │
    │   │   └── EG-004: Child job triggering engine
    │   │       │
    │   │       └── EG-005: Fan-in detection
    │   │
    │   └── EG-006: IScheduledJobManager extensions
    │
    └── EG-007: ScheduledTrigger hierarchy data
        │
        └── EG-008: Integration tests
            │
            └── EG-009: README + docs
```

---

## Stories

### EG-001: Add CausationId + RootCorrelationId to Messaging [M]

Extend the correlation model from flat CorrelationId to a causation chain.

**Files to modify:**
- `src/Headless.Messaging.Abstractions/Messages/Headers.cs` — add `CausationId`, `RootCorrelationId` constants
- `src/Headless.Messaging.Abstractions/MessagingCorrelationScope.cs` — add `CausationId`, `RootCorrelationId` properties + `Begin()` overload
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs` — propagate new headers from scope
- `src/Headless.Messaging.Core/Internal/DirectPublisher.cs` — same
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs` — set `RootCorrelationId` = `CorrelationId` (job execution is root)

**Acceptance Criteria:**
- [ ] `Headers.CausationId` = `"headless-causation-id"` added
- [ ] `Headers.RootCorrelationId` = `"headless-root-corr-id"` added
- [ ] `MessagingCorrelationScope` gains `CausationId` (string?) and `RootCorrelationId` (string) properties
- [ ] `Begin()` accepts `causationId` and `rootCorrelationId` optional params; `rootCorrelationId` defaults to `correlationId`
- [ ] Publishers propagate new headers when scope is active and headers not explicitly set
- [ ] Existing behavior preserved when no scope active
- [ ] Unit tests for new header propagation

### EG-002: Add ParentJobId + RunCondition to ScheduledJob [S]

Entity-level hierarchy support.

**Files to modify:**
- `src/Headless.Messaging.Abstractions/Scheduling/ScheduledJob.cs` — add `ParentJobId`, `RunCondition`
- `src/Headless.Messaging.Abstractions/Scheduling/JobRunCondition.cs` — new enum
- `src/Headless.Messaging.Abstractions/Scheduling/JobExecution.cs` — add `ParentExecutionId`, `RootExecutionId`

**Acceptance Criteria:**
- [ ] `ScheduledJob.ParentJobId` (Guid?) added, nullable, default null
- [ ] `ScheduledJob.RunCondition` (JobRunCondition) added, default `None`
- [ ] `JobRunCondition` enum: `None`, `OnParentSuccess`, `OnParentFailure`, `OnParentCompletion`
- [ ] `JobExecution.ParentExecutionId` (Guid?) added
- [ ] `JobExecution.RootExecutionId` (Guid?) added
- [ ] XML docs on all new members
- [ ] Existing tests compile without changes (new props are optional/defaulted)

### EG-003: Storage Layer — New Columns + Queries [M]

Persistence for hierarchy relationships.

**Files to modify:**
- `src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobStorage.cs` — add `GetChildJobsAsync`, `GetExecutionTreeAsync`, `GetJobDepthAsync`
- `src/Headless.Messaging.PostgreSql/Scheduling/PostgreSqlScheduledJobStorage.cs` — implement new queries
- `src/Headless.Messaging.Core/Scheduling/InMemoryScheduledJobStorage.cs` — implement new queries
- `src/Headless.Messaging.PostgreSql/Migrations/` — add migration for new columns + indexes
- EF configuration for new columns

**Acceptance Criteria:**
- [ ] `GetChildJobsAsync(parentJobId)` — returns direct children
- [ ] `GetExecutionTreeAsync(rootExecutionId)` — returns all executions in tree
- [ ] `GetJobDepthAsync(jobId)` — recursive CTE or iterative query for depth
- [ ] PostgreSQL migration: `parent_job_id`, `run_condition` on `scheduled_jobs`; `parent_execution_id`, `root_execution_id` on `job_executions`
- [ ] Partial indexes on nullable FK columns
- [ ] InMemory provider: LINQ-based implementations
- [ ] Unit tests for InMemory; integration tests for PostgreSQL

### EG-004: Child Job Triggering Engine [L]

Core logic: when parent completes, evaluate and trigger children.

**Files to modify:**
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobExecutor.cs` — add post-execution child triggering
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs` — populate `ParentExecutionId`/`RootExecutionId` on trigger

**Acceptance Criteria:**
- [ ] After parent execution completes, query `GetChildJobsAsync(parentJobId)`
- [ ] Evaluate `RunCondition` against parent execution status
- [ ] For matching children: set `NextRunTime = now`, create execution with `ParentExecutionId` + `RootExecutionId`
- [ ] `RootExecutionId` propagation: if parent has `RootExecutionId`, inherit it; otherwise parent execution is root
- [ ] Depth check before triggering: reject if current depth >= `MaxJobDepth`
- [ ] Fan-out check: count children, reject if >= `MaxFanOut` when scheduling new child
- [ ] `ScheduledTrigger.ParentJobId` populated with actual parent ID (not null)
- [ ] `ScheduledTrigger.ParentExecutionId` and `RootExecutionId` populated
- [ ] Unit tests: parent success triggers OnParentSuccess child, parent failure triggers OnParentFailure child, depth limit enforced

### EG-005: Fan-In Detection [M]

When all sibling child jobs complete, trigger a designated continuation.

**Files to modify:**
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobExecutor.cs` — fan-in check after child completes
- `src/Headless.Messaging.Abstractions/Scheduling/ScheduledJob.cs` — add `IsFanInContinuation` flag

**Acceptance Criteria:**
- [ ] `ScheduledJob.IsFanInContinuation` (bool) — marks this job as "wait for all siblings first"
- [ ] After a child job completes: if any sibling has `IsFanInContinuation = true`, check all non-continuation siblings
- [ ] If all non-continuation siblings are complete (success or failure), trigger the continuation
- [ ] Atomic check to prevent double-triggering under concurrency (use UPDATE ... WHERE + row count)
- [ ] Unit tests: 3 parallel children + 1 continuation; continuation fires only after all 3 complete

### EG-006: IScheduledJobManager Extensions [S]

Public API for scheduling child jobs.

**Files to modify:**
- `src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobManager.cs` — add `ScheduleChildAsync`, `GetExecutionTreeAsync`
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobManager.cs` — implement

**Acceptance Criteria:**
- [ ] `ScheduleChildAsync(name, parentJobName, consumerType, runCondition, payload)` validates: parent exists, depth limit, fan-out limit
- [ ] `GetExecutionTreeAsync(rootExecutionId)` delegates to storage
- [ ] Depth validation: resolve parent chain, check against `SchedulingOptions.MaxJobDepth`
- [ ] Fan-out validation: count existing children, check against `SchedulingOptions.MaxFanOut`
- [ ] Throws `InvalidOperationException` with clear message on limit violations
- [ ] Unit tests for validation edge cases

### EG-007: Update ScheduledTrigger with Hierarchy Data [S]

Populate the existing `ParentJobId` + new fields when dispatching child jobs.

**Files to modify:**
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs` — set hierarchy fields on `ScheduledTrigger`
- `src/Headless.Messaging.Abstractions/ScheduledTrigger.cs` — add `ParentExecutionId`, `RootExecutionId`

**Acceptance Criteria:**
- [ ] `ScheduledTrigger.ParentJobId` = `job.ParentJobId` (populated for child jobs, null for root)
- [ ] `ScheduledTrigger.ParentExecutionId` = `execution.ParentExecutionId`
- [ ] `ScheduledTrigger.RootExecutionId` = `execution.RootExecutionId`
- [ ] Consumer handlers can read hierarchy context from trigger
- [ ] Backward compatible: root jobs have all three as null

### EG-008: Integration Tests [L]

End-to-end hierarchy scenarios.

**Files to modify:**
- `tests/Headless.Messaging.Core.Tests.Integration/Scheduling/` — new test files

**Acceptance Criteria:**
- [ ] Chain test: Job A → Job B → Job C, verify execution order and ParentExecutionId chain
- [ ] Fan-out test: Job A → (B, C, D) parallel, verify all triggered
- [ ] Fan-in test: Job A → (B, C, D) + Continuation E, verify E fires after B+C+D
- [ ] Condition tests: OnParentSuccess skipped on failure, OnParentFailure skipped on success
- [ ] Depth limit test: reject scheduling beyond MaxJobDepth
- [ ] Causation headers test: verify `headless-causation-id` and `headless-root-corr-id` propagate through job→message→child job chain
- [ ] Uses Testcontainers + PostgreSQL

### EG-009: Configuration + Documentation [S]

**Files to modify:**
- `src/Headless.Messaging.Core/Scheduling/SchedulingOptions.cs` — add `MaxJobDepth`, `MaxFanOut`
- `src/Headless.Messaging.Core/README.md` — document hierarchy features
- `src/Headless.Messaging.Abstractions/README.md` — document new types

**Acceptance Criteria:**
- [ ] `SchedulingOptions.MaxJobDepth` defaults to 5
- [ ] `SchedulingOptions.MaxFanOut` defaults to 10
- [ ] README documents: chaining API, fan-in pattern, causation headers
- [ ] Code examples for common patterns (A→B chain, fan-out, fan-in)

---

## Migration Strategy

### Backward Compatibility

All new columns are **nullable** or have **defaults** — zero breaking changes to existing consumers:

- `ScheduledJob.ParentJobId` = null (no parent)
- `ScheduledJob.RunCondition` = `None` (default)
- `JobExecution.ParentExecutionId` = null
- `JobExecution.RootExecutionId` = null
- `ScheduledTrigger.ParentJobId` = null (already exists, no change for root jobs)
- New headers are only set when scope provides them

### EF Migration

Single migration adding columns to existing tables. No data migration needed — all existing rows get null/default values.

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| Recursive CTE for depth check on every child schedule | Perf hit on deep graphs | Cache depth on entity or limit CTE depth to MaxJobDepth |
| Double-triggering of fan-in continuation under concurrency | Duplicate execution | Atomic UPDATE with WHERE condition on execution count |
| Circular parent references | Infinite loop | Validate acyclicity in `ScheduleChildAsync` via depth traversal |
| Storage provider drift (PostgreSQL vs InMemory behavior) | Test gaps | Integration tests against both providers |
| Over-engineering fan-in | Complexity with low usage | IsFanInContinuation is the simplest possible model; no DAG solver |

---

## What This Replaces

- **TickerQ `ParentId`** — Replaced by `ScheduledJob.ParentJobId` (same concept, cleaner integration)
- **TickerQ `RunCondition`** — Replaced by `JobRunCondition` (4 values instead of 6 — removed `OnTimeout`, `OnCancelled`, `Custom`)
- **TickerQ `FluentChainTickerBuilder`** — NOT replaced. Use `IScheduledJobManager.ScheduleChildAsync()` directly
- **TickerQ `_ParentIdIndex`** — NOT replaced. Runtime parent index was for cancellation propagation — use `GetChildJobsAsync` queries instead
- **`ScheduledTrigger.ParentJobId`** (always-null) — Now populated with actual parent ID for child jobs

---

## Unresolved Questions

1. **Should `RootExecutionId` be eagerly set or lazily computed?** Setting it on every child execution means one extra column write. Computing it from `ParentExecutionId` chain means a recursive query. Recommendation: eager (column write is cheaper than recursive query at read time).

2. **Cancellation propagation** — When a parent job is cancelled, should all pending child jobs be cancelled? TickerQ did this via `_ParentIdIndex`. We'd need `CancelChildJobsAsync(parentJobId)` on storage. Deferred — implement when cancellation API is built.

3. **Fan-in atomicity** — The "check all siblings complete → trigger continuation" needs to be atomic under concurrent child completions. PostgreSQL: `UPDATE ... SET next_run_time = now WHERE id = continuation_id AND (SELECT COUNT(*) FROM job_executions WHERE ...) = sibling_count` in a single statement. InMemory: `lock`.

4. **Should `CausationId` on messages published from a job handler be the execution ID or the job name?** Recommendation: execution ID — it's more specific and enables tracing to the exact run.
