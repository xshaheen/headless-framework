# feat: Ticker→Messaging Full Merge — Remaining Gaps, Dashboard, Deprecation & Removal

> **Scope:** Everything needed to fully remove Headless.Ticker packages after existing plans (correlation + hardening) are implemented. Consumer→publish correlation, OpenTelemetry scheduling, dashboard merge (Ticker UI as base), Ticker deprecation, Ticker removal.
>
> **Prerequisites:** Phase 1+2 complete. Correlation plan (US-006–010) and Hardening plan (US-017–029) are planned but not yet implemented. This plan can be implemented in parallel where noted.
>
> **Not in scope:** Parent-child job hierarchy (decided not needed), SqlServer storage, GZip payload compression, generic entity extensibility.

## Overview

After Phase 1+2 (core scheduling), the correlation plan (job→message), and the hardening plan (stale recovery, timeout, one-time, InMemory, compiled delegates), four gaps remain before Ticker can be fully removed:

1. **Consumer→publish correlation** — when any `IConsume<T>` handler publishes messages, auto-inherit correlation from consumed message
2. **OpenTelemetry scheduling** — instrument scheduled job lifecycle in `Headless.Messaging.OpenTelemetry`
3. **Dashboard merge** — merge Ticker Dashboard (Vue 3 + TS, SignalR, rich auth) with Messaging Dashboard (Vue 2, REST, pub/sub only) into one unified dashboard using Ticker's UI as the base
4. **Ticker deprecation & removal** — mark all 7 packages obsolete, write migration guide, delete source

### Redis Node Heartbeat Decision

Ticker had `NodeHeartBeatBackgroundService` (Redis-based) for multi-node coordination. **Not needed.** Messaging uses `SELECT FOR UPDATE SKIP LOCKED` for atomic distributed job acquisition plus `LockHolder`/`LockedAt` tracking on `ScheduledJob`. This is more robust than Redis heartbeat — no additional infrastructure dependency, atomic at the database level, and stale lock recovery is handled by `StaleJobRecoveryService` (hardening plan US-021).

---

## Technical Approach

### Consumer→Publish Correlation

The correlation plan creates `MessagingCorrelationScope` (AsyncLocal) and sets it in `ScheduledJobDispatcher`. To extend this to ALL consumers, we set the scope in `CompiledMessageDispatcher.DispatchAsync()` — the single point where all message consumers are invoked:

```csharp
// CompiledMessageDispatcher.DispatchAsync() — BEFORE consumer invocation:
var correlationId = consumeContext.CorrelationId
    ?? consumeContext.Headers?.GetValueOrDefault(Headers.CorrelationId)
    ?? consumeContext.MessageId;

using var correlationScope = MessagingCorrelationScope.Begin(
    correlationId,
    consumeContext.Headers?.GetCorrelationSequence() ?? 0
);

// consumer.Consume(consumeContext, cancellationToken)
// Any messages published by the consumer auto-inherit correlation from scope
```

This works because `MessagingCorrelationScope` (from correlation plan US-001) is AsyncLocal-based and publishers already check it (correlation plan US-008). No changes to publishers needed — just setting the scope at the right place.

### OpenTelemetry Scheduling Integration

**Pattern:** Messaging uses `System.Diagnostics.DiagnosticSource` for instrumentation. The OpenTelemetry package subscribes to diagnostic events and creates OpenTelemetry Activities.

**Step 1:** Add `DiagnosticSource.Write()` calls to `SchedulerBackgroundService` and `ScheduledJobDispatcher` (same pattern as `OutboxPublisher._TracingBefore/After`).

**Step 2:** Subscribe in `Headless.Messaging.OpenTelemetry.DiagnosticListener` and create Activities with tags ported from Ticker:

| Activity Name | Kind | Tags |
|---|---|---|
| `Messaging.ScheduledJob/{JobName}/Dispatch` | Internal | `messaging.job.name`, `messaging.job.execution_id`, `messaging.job.attempt`, `messaging.job.cron_expression` |
| `Messaging.ScheduledJob/{JobName}/Completed` | Internal | `messaging.job.name`, `messaging.job.execution_id`, `messaging.job.duration_ms`, `messaging.job.success` |
| `Messaging.ScheduledJob/{JobName}/Failed` | Internal | `messaging.job.name`, `messaging.job.execution_id`, `messaging.job.error_type`, `messaging.job.retry_count` |

### Dashboard Merge Strategy

**Approach:** Use Ticker Dashboard's Vue 3 + TypeScript frontend as the base. Replace Messaging Dashboard's Vue 2 frontend entirely. Merge backends.

**Why Ticker's UI as base:**
- Vue 3 + TypeScript vs Vue 2 + JavaScript — Ticker is more modern
- SignalR real-time updates (Ticker has it, Messaging doesn't)
- Rich 5-mode auth system (None, Basic, ApiKey, Host, Custom)
- Better UX: composables, stores, TypeScript types

**Backend merge:**
- Keep Messaging Dashboard's pub/sub endpoints (`/api/published/*`, `/api/received/*`, `/api/subscriber`, `/api/nodes`)
- Port Ticker's scheduling endpoints adapted to `ScheduledJob`/`JobExecution` entities
- Port Ticker's auth system (`AuthService`, `AuthMiddleware`, 5 modes)
- Port Ticker's SignalR hub for real-time job notifications

**Frontend merge:**
- Base: Ticker Dashboard's Vue 3 + TS scaffold (App.vue, router, stores, composables, auth)
- Adapt: Ticker's scheduling pages (jobs, executions, graphs) to use Messaging API
- Port: Messaging's pub/sub pages (Published, Received, Subscriber, Nodes) rewritten in Vue 3 + TS
- Unify: Single navigation with both Messaging and Scheduling sections

**Endpoint simplification:** Ticker had separate Time Ticker and Cron Ticker endpoints. Messaging unifies into `ScheduledJob` with `Type` (Recurring/OneTime), so endpoints simplify:

| Ticker Endpoint | Messaging Equivalent |
|---|---|
| `/api/time-tickers`, `/api/cron-tickers` | `/api/scheduled-jobs` |
| `/api/cron-ticker-occurrences/{id}` | `/api/scheduled-jobs/{id}/executions` |
| `/api/time-ticker/add`, `/api/cron-ticker/add` | `/api/scheduled-jobs` (POST) |
| `/api/ticker/cancel` | `/api/scheduled-jobs/{name}/cancel` |
| `/api/ticker-host/status` | `/api/scheduler/status` |

---

## Stories

### Phase 1: Consumer→Publish Correlation Extension

> **Depends on:** Correlation plan US-006 (MessagingCorrelationScope class must exist)

#### US-030: Set MessagingCorrelationScope in CompiledMessageDispatcher [S]

Extend ambient correlation from scheduled jobs to ALL message consumers.

**Files to Study:**
- `src/Headless.Messaging.Core/Internal/CompiledMessageDispatcher.cs` — consumer invocation point
- `src/Headless.Messaging.Core/Internal/ISubscribeInvoker.cs:254-270` — existing callback correlation

**Acceptance Criteria:**
- [ ] `MessagingCorrelationScope.Begin()` called before `consumer.Consume()` in `CompiledMessageDispatcher`
- [ ] Scope uses consumed message's `CorrelationId` (fall back to `MessageId` if null)
- [ ] Scope disposed after consumer completes (in `finally`)
- [ ] Published messages from consumer inherit correlation automatically (via publishers checking scope — already done in correlation plan US-008)
- [ ] Callback correlation still works (explicit headers override ambient scope)

#### US-031: Unit tests for consumer→publish correlation [M]

**Files to Study:**
- `tests/Headless.Messaging.Core.Tests.Unit/`

**Acceptance Criteria:**
- [ ] Consumer publishes message → inherits consumed message's CorrelationId
- [ ] Consumer publishes multiple messages → incrementing CorrelationSequence
- [ ] Consumer passes explicit CorrelationId header → overrides ambient scope
- [ ] No consumed message context (e.g., direct publish outside consumer) → existing behavior
- [ ] `TestBase`, `AbortToken`, `should_*_when_*` naming

---

### Phase 2: OpenTelemetry Scheduling Integration

> **Can be implemented in parallel with other phases**

#### US-032: Add DiagnosticSource events to scheduling pipeline [M]

Emit diagnostic events from scheduler for OpenTelemetry to subscribe to.

**Files to Study:**
- `src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs` — job polling and execution
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs` — handler invocation
- `src/Headless.Messaging.Core/Diagnostics/MessageDiagnosticListenerNames.cs` — existing event names
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs:262-324` — existing tracing pattern

**Acceptance Criteria:**
- [ ] `DiagnosticListener` instance in `ScheduledJobDispatcher` (same pattern as `OutboxPublisher`)
- [ ] Events: `WriteScheduledJobDispatchBefore`, `WriteScheduledJobDispatchAfter`, `WriteScheduledJobDispatchError`
- [ ] Event data carries: job name, execution ID, attempt number, scheduled time
- [ ] Error event includes exception details
- [ ] No performance impact when no subscriber (guarded by `IsEnabled`)

#### US-033: Subscribe to scheduling events in Messaging.OpenTelemetry [M]

Create OpenTelemetry Activities for scheduled job execution.

**Files to Study:**
- `src/Headless.Messaging.OpenTelemetry/DiagnosticListener.cs` — existing subscriber pattern
- `src/Headless.Messaging.OpenTelemetry/Setup.cs` — registration
- `src/Headless.Ticker.OpenTelemetry/OpenTelemetryInstrumentation.cs` — Ticker's tag naming

**Acceptance Criteria:**
- [ ] Subscribe to `WriteScheduledJobDispatch*` events in `DiagnosticListener`
- [ ] Create `Activity` with name `Messaging.ScheduledJob/{JobName}/Dispatch` (ActivityKind.Internal)
- [ ] Tags: `messaging.job.name`, `messaging.job.execution_id`, `messaging.job.attempt`, `messaging.job.duration_ms`, `messaging.job.success`
- [ ] Error: set `ActivityStatusCode.Error` with exception details
- [ ] Metrics: `messaging.scheduled_job.executions` counter, `messaging.scheduled_job.duration` histogram

#### US-034: Unit tests for scheduling instrumentation [S]

**Files to Study:**
- `tests/Headless.Messaging.Core.Tests.Unit/`

**Acceptance Criteria:**
- [ ] DiagnosticSource events emitted on dispatch/success/failure
- [ ] Events not emitted when no subscriber (performance guard)
- [ ] `TestBase`, `AbortToken`, `should_*_when_*` naming

---

### Phase 3: Dashboard Merge

> **Can start in parallel. Auth + backend first, then frontend.**

#### US-035: Port auth system to Messaging Dashboard [M]

Replace simple auth policy with Ticker's 5-mode auth system.

**Files to Study:**
- `src/Headless.Ticker.Dashboard/AuthConfig.cs`, `AuthService.cs`, `IAuthService.cs`, `AuthMiddleware.cs`
- `src/Headless.Messaging.Dashboard/DashboardOptions.cs` — current simple auth
- `src/Headless.Messaging.Dashboard/Setup.cs` — DI registration

**Acceptance Criteria:**
- [ ] `AuthConfig` with 5 modes: None, Basic, ApiKey, Host, Custom
- [ ] `AuthService` with `CryptographicOperations.FixedTimeEquals` for secure comparison
- [ ] `AuthMiddleware` supporting `Authorization` header and `access_token` query param (for WebSocket)
- [ ] `DashboardOptions` extended with fluent auth config: `WithBasicAuth()`, `WithApiKey()`, `WithHostAuthentication()`, `WithCustomAuth()`
- [ ] Backward compatible — existing `AuthorizationPolicy` still works as `WithHostAuthentication(policy)`

#### US-036: Create scheduling dashboard repository [M]

Backend queries for scheduled jobs and executions in dashboard.

**Files to Study:**
- `src/Headless.Ticker.Dashboard/TickerDashboardRepository.cs` — Ticker's query patterns
- `src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobStorage.cs` — existing storage interface
- `src/Headless.Messaging.Dashboard/RouteActionProvider.cs` — existing endpoint pattern

**Acceptance Criteria:**
- [ ] `ISchedulingDashboardRepository` interface: GetJobsPaginated, GetExecutionsPaginated, GetJobGraphData, GetExecutionGraphData, GetSchedulerStatus
- [ ] Implementation delegates to `IScheduledJobStorage` + additional queries
- [ ] Graph data: status counts by date range (adapt Ticker's `TickerGraphData` pattern)
- [ ] Per-machine job counts (using `LockHolder` field)
- [ ] Registered in DI when `IScheduledJobStorage` is present

#### US-037: Add scheduling REST endpoints to Messaging Dashboard [L]

Port Ticker's scheduling endpoints adapted to Messaging entities.

**Files to Study:**
- `src/Headless.Ticker.Dashboard/DashboardEndpoints.cs` — Ticker's 30+ endpoints
- `src/Headless.Messaging.Dashboard/RouteActionProvider.cs` — existing endpoint pattern
- `src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobManager.cs` — runtime operations

**Acceptance Criteria:**
- [ ] Job endpoints: GET `/api/scheduled-jobs` (paginated), GET `/api/scheduled-jobs/{name}`, POST `/api/scheduled-jobs` (create one-time), PUT `/api/scheduled-jobs/{name}` (update), DELETE `/api/scheduled-jobs/{name}`
- [ ] Execution endpoints: GET `/api/scheduled-jobs/{name}/executions` (paginated), GET `/api/scheduled-jobs/{name}/graph-data`
- [ ] Operation endpoints: POST `/api/scheduled-jobs/{name}/trigger`, POST `/api/scheduled-jobs/{name}/enable`, POST `/api/scheduled-jobs/{name}/disable`
- [ ] Status endpoints: GET `/api/scheduler/status`, GET `/api/scheduler/next-run`
- [ ] All endpoints respect auth middleware
- [ ] Endpoints return 404 when `IScheduledJobStorage` not registered (scheduling-only guard)

#### US-038: Add SignalR hub for real-time notifications [M]

Port Ticker's SignalR notification hub for live job updates.

**Files to Study:**
- `src/Headless.Ticker.Dashboard/TickerQNotificationHub.cs` — hub definition
- `src/Headless.Ticker.Dashboard/TickerQNotificationHubSender.cs` — sender pattern
- `src/Headless.Messaging.Dashboard/Setup.cs` — DI registration

**Acceptance Criteria:**
- [ ] `MessagingNotificationHub` with `JoinGroup`/`LeaveGroup`/`GetStatus` methods
- [ ] `IMessagingNotificationHubSender` interface with notifications: `JobExecutionStarted`, `JobExecutionCompleted`, `JobExecutionFailed`, `JobCreated`, `JobUpdated`, `JobDeleted`, `SchedulerStatusChanged`
- [ ] Hub sender called from `SchedulerBackgroundService` on job lifecycle events
- [ ] Auth validated on WebSocket connection (via `access_token` query param)
- [ ] Hub mapped at `/messaging-hub` endpoint

#### US-039: Replace frontend with unified Vue 3 + TypeScript dashboard [L]

Replace Messaging Dashboard's Vue 2 frontend entirely with Ticker's Vue 3 + TS scaffold. Adapt scheduling pages and rewrite pub/sub pages. K8s node discovery merged into main dashboard.

**Files to Study:**
- `src/Headless.Ticker.Dashboard/frontend/` — Vue 3 + TS source (App.vue, router, stores, composables, auth, types)
- `src/Headless.Messaging.Dashboard/frontend/` — Vue 2 source (pages, router, store)
- `src/Headless.Messaging.Dashboard.K8s/` — K8s node discovery to merge

**Acceptance Criteria:**
- [ ] Vue 3 + TypeScript + Vite build pipeline
- [ ] Auth composable (`useAuth`) supporting all 5 modes
- [ ] Dashboard store with SignalR connection management
- [ ] Scheduling pages: Jobs list, Job detail, Executions list, Execution graph, Scheduler status
- [ ] Pages adapted from Ticker's TimeTickerEntity/CronTickerEntity to Messaging's `ScheduledJob`/`JobExecution`
- [ ] Home page with unified stats (pub/sub + scheduling)
- [ ] Published/Received pages with status filtering, pagination, requeue/delete (Vue 3 + TS)
- [ ] Subscriber page showing consumer groups and topics
- [ ] Nodes page with Consul/K8s discovery merged in
- [ ] All pages use TypeScript types and Vue 3 Composition API
- [ ] Unified navigation with Scheduling and Messaging sections
- [ ] Pre-built dist assets committed (same pattern as existing dashboards)

#### US-041: Dashboard integration tests [M]

**Files to Study:**
- `tests/Headless.Messaging.Dashboard.Tests.Unit/` (if exists)

**Acceptance Criteria:**
- [ ] Auth middleware tests: all 5 modes (None, Basic, ApiKey, Host, Custom)
- [ ] Scheduling endpoint tests: CRUD, trigger, enable/disable
- [ ] SignalR hub tests: connection, group join/leave, notifications
- [ ] Endpoint returns 404 when scheduling not configured
- [ ] `TestBase`, `AbortToken`, `should_*_when_*` naming

---

### Phase 4: Ticker Removal

> **Greenfield — skip deprecation and migration guide, go directly to removal after all features are implemented and tested.**

#### US-044: Remove all Ticker packages [M]

Delete all Ticker source, tests, and references.

**Files to Study:**
- `headless-framework.slnx` or `*.sln` — solution file
- `Directory.Packages.props` — package versions
- All `src/Headless.Ticker.*` directories
- All `tests/Headless.Ticker.*` directories

**Acceptance Criteria:**
- [ ] Delete `src/Headless.Ticker.Abstractions/`
- [ ] Delete `src/Headless.Ticker.Core/`
- [ ] Delete `src/Headless.Ticker.EntityFramework/`
- [ ] Delete `src/Headless.Ticker.Dashboard/`
- [ ] Delete `src/Headless.Ticker.OpenTelemetry/`
- [ ] Delete `src/Headless.Ticker.Caching.Redis/`
- [ ] Delete `src/Headless.Ticker.SourceGenerator/`
- [ ] Delete all `tests/Headless.Ticker.*` test projects
- [ ] Remove from solution file(s)
- [ ] Remove Ticker package references from `Directory.Packages.props`
- [ ] Remove NCrontab dependency (Messaging uses Cronos)
- [ ] Solution builds cleanly with zero Ticker references
- [ ] Move `docs/merge/` files to `docs/archive/` (historical reference)

---

## Dependencies Between Plans

```
Phase 1+2 (DONE)
    ├── Correlation Plan (US-006–010)
    │       └── This Plan: Phase 1 (US-030–031) — consumer→publish correlation
    ├── Hardening Plan (US-017–029)
    │       └── This Plan: Phase 3 (US-035–041) — dashboard shows hardening features
    ├── This Plan: Phase 2 (US-032–034) — OpenTelemetry (independent)
    └── This Plan: Phase 3 (US-035–041) — Dashboard merge (independent backend)

This Plan: Phase 4 (US-044) — Removal (after all above, greenfield = no deprecation)
```

---

## Resolved Questions

1. **Dashboard deployment model** — Single package (`Headless.Messaging.Dashboard`). Simpler, no split needed.

2. **Deprecation period** — Greenfield, no production users. Skip deprecation (US-042) and migration guide (US-043). Go directly from US-041 to US-044 (removal).

3. **Dashboard K8s integration** — Merge K8s node discovery into the main dashboard (US-039 Nodes page). `Headless.Messaging.Dashboard.K8s` functionality absorbed.

---

## References

### Internal
- `src/Headless.Messaging.Core/Internal/CompiledMessageDispatcher.cs` — consumer invocation point for correlation
- `src/Headless.Messaging.OpenTelemetry/DiagnosticListener.cs` — existing OTel subscriber pattern
- `src/Headless.Messaging.Dashboard/RouteActionProvider.cs` — existing dashboard endpoints
- `src/Headless.Ticker.Dashboard/DashboardEndpoints.cs` — Ticker's 30+ endpoints to port
- `src/Headless.Ticker.Dashboard/TickerQNotificationHub.cs` — SignalR hub to port
- `src/Headless.Ticker.OpenTelemetry/OpenTelemetryInstrumentation.cs` — Ticker's Activity tags
- `docs/merge/TICKER-MESSAGING-INTEGRATION-QUICKREF.md` — existing migration reference
- `docs/merge/INSTITUTIONAL-LEARNINGS-TICKER-MESSAGING-INTEGRATION.md` — gotchas

### Prior Plans
- `docs/plans/2026-02-07-feat-job-execution-correlation-plan.md` — MessagingCorrelationScope design (US-006–010)
- `docs/plans/2026-02-07-feat-messaging-scheduling-hardening-plan.md` — stale recovery, timeout, one-time, InMemory (US-017–029)
- `plans/2026-02-05-feat-unified-messaging-scheduling-plan.md` — Phase 1+2 (complete)
