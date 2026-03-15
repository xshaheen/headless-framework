---
title: "feat: Add property-level audit log subsystem"
type: feat
date: 2026-03-15
---

> **Verification gate:** Before claiming any task or story complete — run the plan's `verification_command` and confirm PASS. Do not mark complete based on reading code alone.

# feat: Add property-level audit log subsystem

## Overview

Add property-level entity change tracking to headless-framework via EF Core ChangeTracker for opted-in entities, plus explicit `IAuditLog` for non-mutation events (data access, PII reveals). Framework owns write path only — consumers own reads, retention, partitioning.

Two new packages following the established abstraction + provider pattern:

- `Headless.AuditLog.Abstractions` — marker, attributes, options, contracts
- `Headless.AuditLog.EntityFramework` — ChangeTracker capture, entity, storage

Plus one prerequisite addition to `Headless.Core`: `ICorrelationIdProvider`.

## Problem Statement / Motivation

Compliance (GDPR, HIPAA, SOC2) and operational auditability require knowing **who changed what, when, and from what to what**. The framework already tracks *who* and *when* via `ICreateAudit`/`IUpdateAudit`/`IDeleteAudit`, but not *what changed* at the property level. Consumers currently build ad-hoc solutions or use third-party libraries that don't integrate with the existing `HeadlessDbContext` pipeline.

## Proposed Solution

Piggyback on the existing `HeadlessEntityModelProcessor.ProcessEntries()` pipeline — after audit fields are populated but before `SaveChanges`. The ChangeTracker already has all before/after values at this point, requiring zero extra DB queries.

### Architecture

```
SaveChanges() called
    │
    ▼
HeadlessEntityModelProcessor.ProcessEntries(db)
    ├── Existing: set GuidId, TenantId, CreateAudit, UpdateAudit, DeleteAudit, ConcurrencyStamp
    ├── Existing: collect emitter messages
    └── NEW: IAuditChangeCapture.CaptureChanges() → AuditLogEntryData[]
    │
    ▼
HeadlessDbContext receives ProcessBeforeSaveReport (now includes AuditEntries)
    ├── NEW: Add AuditLogEntry entities to context (via IAuditLogStore)
    └── Existing: transaction/message/save logic
    │
    ▼
base.SaveChanges() — audit entries commit atomically with entity changes
```

## Technical Considerations

### Package Dependencies (Corrected from Original Spec)

The abstractions package is **standalone** — no coupling to framework internals:

```
Headless.AuditLog.Abstractions
  ├── Microsoft.Extensions.Options
  └── Microsoft.Extensions.DependencyInjection.Abstractions

Headless.AuditLog.EntityFramework
  ├── Headless.AuditLog.Abstractions
  └── Headless.Orm.EntityFramework (brings Headless.Core + Headless.Domain + EF Core)

Headless.Core (existing)
  └── NEW: ICorrelationIdProvider + ActivityCorrelationIdProvider (System.Diagnostics.Activity — no new deps)
```

### Design Corrections from Spec Review

Research against the actual codebase surfaced several corrections:

1. **`UserId?.Value` → `UserId?.ToString()`**: The `UserId`/`AccountId` primitives are source-generated types implementing `IPrimitive<string>`. Existing code (e.g., `UserPermissionGrantProvider.cs:26`) uses `.ToString()`, not `.Value`.

2. **`EfAuditLogStore(DbContext dbContext)` DI issue**: Consumers register their concrete `TDbContext` via `AddDbContext<TMyContext>()` — not base `DbContext`. The scoped DI container won't resolve `DbContext`. **Solution**: Change `IAuditLogStore` to accept a context parameter:

   ```csharp
   public interface IAuditLogStore
   {
       void Save(object context, IReadOnlyList<AuditLogEntryData> entries);
       Task SaveAsync(object context, IReadOnlyList<AuditLogEntryData> entries, CancellationToken ct);
   }
   ```

   HeadlessDbContext passes `this` as the context. `EfAuditLogStore` casts to `DbContext` internally. Non-EF implementations ignore the context parameter.

3. **`EfAuditLog(DbContext dbContext)` same issue**: For explicit audit logging, register a forward in `AddHeadlessDbContext<T>()`:

   ```csharp
   services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TDbContext>());
   ```

   This enables `EfAuditLog` to inject `DbContext`. Alternative: use `IServiceProvider` and resolve lazily.

4. **Abstractions package should NOT depend on `Headless.Domain` or `Headless.Core`**: The interfaces only use primitive types (`string?`, `object?`, `DateTimeOffset`). Framework-specific types (`UserId`, `ICurrentUser`) are only needed by the EF implementation.

5. **`CoreSaveChangesAsync` integration**: The audit store call goes right after `ProcessEntries()`, before the transaction/emitter branching logic. Since `EfAuditLogStore.Save()` only calls `Set<AuditLogEntry>().Add()` (no SaveChanges), entries are added to the context and persist with the same save — regardless of which transaction path is taken.

### Performance

- **Reflection**: Attribute lookups cached in static `ConcurrentDictionary` per `PropertyInfo` — one-time cost per property per AppDomain
- **Serialization**: JSON serialization happens in the `Save()` call, inside the SaveChanges hot path. Use `JsonSerializerOptions` singleton, not per-call allocation
- **ChangeTracker iteration**: Second pass over entries after ProcessEntries. Consider integrating into the existing `foreach` loop if profiling shows overhead

### JSON Column Strategy

String columns with value converters by default (universal portability). Optional `JsonColumnType` override for native JSON queries:
- PostgreSQL: `"jsonb"`
- SQL Server 2025+: `"json"`

### Sensitive Data

Three-tier strategy per property:
- **Redact**: `"***"` — you know it changed, not to what
- **Exclude**: Omitted entirely — as if the property doesn't exist
- **Transform**: Consumer-provided function (hash, mask, tokenize)

Global default configurable, per-property override via `[AuditSensitive(Strategy = ...)]`.

## System-Wide Impact

### Interaction Graph

`SaveChanges()` → `ProcessEntries()` → `IAuditChangeCapture.CaptureChanges()` → `IAuditLogStore.Save()` → `base.SaveChanges()`. The capture runs after all entity processors (CreateAudit, UpdateAudit, etc.) complete, so captured values reflect the final state including auto-populated fields.

### Error Propagation

If `CaptureChanges()` throws, it propagates up through `SaveChanges()` — entity changes also fail (correct behavior: audit and entities are atomic). The `SensitiveValueTransformer` is called inside capture — a throwing transformer aborts the save. Document: transformers must be pure, non-throwing functions.

### State Lifecycle Risks

- **Recursion**: `AuditLogEntry` has `[AuditIgnore]` to prevent recursive capture when `AuditAllEntities = true`
- **Atomicity**: Audit entries are in the same DbContext/transaction — if save fails, audit entries roll back too
- **Owned entity orphans**: Not possible — owned entities are tracked by EF Core's change tracker and tied to their owner's lifecycle

### API Surface Parity

No existing interfaces expose equivalent functionality. This is a new subsystem.

### Integration Test Scenarios

1. Create entity → SaveChanges → query AuditLogEntry table → verify NewValues populated, no OldValues
2. Update entity → verify OldValues + NewValues + ChangedFields (only modified properties)
3. SaveChanges throws → verify no AuditLogEntry rows persisted (rollback)
4. `[AuditSensitive(Strategy = Redact)]` property → verify value is `"***"` in stored entry
5. Owned entity update → verify EntityType = `"Owner.OwnedType"`, EntityId = owner's PK

## Stories

> Full story details in companion PRD: [`2026-03-15-feat-audit-log-subsystem-plan.prd.json`](./2026-03-15-feat-audit-log-subsystem-plan.prd.json)

| ID | Title | Size |
|----|-------|------|
| US-001 | Add ICorrelationIdProvider to Headless.Core | XS |
| US-002 | Create Headless.AuditLog.Abstractions package | S |
| US-003 | Create AuditLogEntry entity and configuration | S |
| US-004 | Implement EfAuditChangeCapture | L |
| US-005 | Implement EfAuditLogStore and EfAuditLog | S |
| US-006 | Integrate with HeadlessEntityModelProcessor, HeadlessDbContext, and HeadlessIdentityDbContext | M |
| US-007 | DI registration and Setup.cs for both packages | S |
| US-008 | Unit tests for EfAuditChangeCapture | L |
| US-009 | Integration tests for full round-trip | M |
| US-010 | README.md files for both packages | S |

## Final Acceptance Criteria

### Functional Requirements

- [ ] Entities implementing `IAuditTracked` have property-level changes captured automatically on SaveChanges
- [ ] `AuditAllEntities` mode captures all entities unless `[AuditIgnore]` is applied
- [ ] `[AuditSensitive]` properties are handled per Redact/Exclude/Transform strategy
- [ ] `[AuditIgnore]` properties are excluded from all audit dictionaries
- [ ] `IAuditLog.LogAsync()` creates explicit audit entries for non-mutation events
- [ ] Audit entries commit atomically with entity changes (same transaction)
- [ ] Owned entities inherit auditability from owner with correct EntityType/EntityId
- [ ] When audit subsystem is not registered, zero overhead (null checks, no ChangeTracker scan)

### Non-Functional Requirements

- [ ] Property attribute lookups cached — no repeated reflection after first access
- [ ] JSON serializer options shared singleton
- [ ] No sync-over-async: `CoreSaveChanges` calls `Save`, `CoreSaveChangesAsync` calls `SaveAsync`

### Quality Gates

- [ ] Unit test coverage ≥85% for EfAuditChangeCapture
- [ ] Integration tests pass against SQLite (unit) and PostgreSQL (integration via Testcontainers)
- [ ] `dotnet build` succeeds for both new packages and modified existing packages
- [ ] CSharpier formatting passes
- [ ] XML docs on all public APIs

## Alternative Approaches Considered

1. **EF Core SaveChanges interceptor**: Would bypass the HeadlessEntityModelProcessor pipeline. Loses the "audit fields already populated" guarantee. Rejected.
2. **Separate DbContext for audit entries**: Loses atomicity (audit entry persists even if entity save fails). Rejected for V1.
3. **Event sourcing**: Overkill for a framework library. Consumers can implement on top of the audit log if needed.
4. **ABP-style request-scoped audit**: Too tightly coupled to HTTP lifecycle. The ChangeTracker-based approach works for background jobs, message handlers, etc.

## Dependencies & Prerequisites

- `Headless.Core` must be modified first (Story 0: `ICorrelationIdProvider`)
- `Headless.Orm.EntityFramework` must be modified (Story 6: processor + context integration)
- Both modifications are backward-compatible (optional dependencies, null-safe)

## Open Design Questions (Resolve Before/During Implementation)

### Critical

**Q1: HeadlessIdentityDbContext duplication.**
`HeadlessIdentityDbContext` (`src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs`) duplicates the entire `CoreSaveChangesAsync`/`CoreSaveChanges` pipeline. Audit integration must be added to **both** contexts, or Identity-derived DbContexts silently skip audit capture.
→ **Decision needed**: modify both and flag refactoring the duplication as follow-up, OR extract shared helper first.

**Q2: Error handling policy for capture failures.**
If `IAuditChangeCapture.CaptureChanges()` throws (e.g., bad transformer), should it:
- (a) Propagate and kill SaveChanges (audit failure = business failure)
- (b) Log warning, skip audit, let domain data persist
→ **Recommended**: (b) for V1 — wrap capture in try/catch, log, continue. Audit shouldn't block business operations.

**Q3: Framework-managed property filtering.**
`ProcessEntries` auto-sets `DateCreated`, `DateUpdated`, `ConcurrencyStamp`, `CreatedById`, `UpdatedById`, etc. These appear as "changes" in the ChangeTracker. Should they be excluded from audit diffs?
→ **Recommended**: Exclude `ConcurrencyStamp` always. Add a default `PropertyFilter` that excludes known framework audit properties (`DateCreated`, `DateUpdated`, `DateDeleted`, `DateSuspended`, `CreatedById`, `UpdatedById`, `DeletedById`, `SuspendedById`, `ConcurrencyStamp`). Consumers can override.

### Important

**Q4: Soft-delete action semantics.**
When `IDeleteAudit.IsDeleted` toggles to `true`, ChangeTracker state is `Modified` (not `Deleted`). The audit `Action` should be semantic (`entity.soft_deleted`) not literal (`entity.updated`). Same for restore.
→ **Recommended**: Detect `IsDeleted`/`IsSuspended` transitions in capture and map to `entity.soft_deleted`/`entity.restored`/`entity.suspended`/`entity.unsuspended`.

**Q5: AuditLogEntry multi-tenant isolation.**
Should `AuditLogEntry` implement `IMultiTenant` for automatic query filter isolation? If not, consumers could see cross-tenant audit entries.
→ **Recommended**: No — `AuditLogEntry` is not an `IEntity` and lives outside the domain model. The `TenantId` column + index supports manual filtering. Adding `IMultiTenant` would couple the audit entity to the framework's query filter system, which the spec explicitly avoids (consumers own reads).

**Q6: `IAuditLog.LogAsync` persistence guarantee.**
`LogAsync` adds to the DbContext but doesn't call SaveChanges. If the caller never calls SaveChanges, the entry is silently lost.
→ **Recommended**: Document clearly. For standalone persistence (background jobs), consumer must call SaveChanges explicitly after LogAsync.

## Risk Analysis & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| ChangeTracker second pass adds SaveChanges latency | Medium | Measure. If significant, merge into existing ProcessEntries loop |
| JSON serialization in hot path | Low | Cached JsonSerializerOptions. Values are typically small (few properties per entity) |
| `SensitiveValueTransformer` throws | High | Wrap in try/catch, fallback to Redact strategy, log warning |
| Multiple DbContexts in scope | Medium | Document: `EfAuditLog` resolves single `DbContext` from DI. Multi-context scenarios need explicit registration |
| HeadlessIdentityDbContext not updated | High | Must modify both DbContext classes. Flag duplication refactor as follow-up |
| Framework-managed properties pollute audit diffs | Medium | Default PropertyFilter excludes ConcurrencyStamp + known audit fields |

## Sources & References

### Internal References

- `HeadlessEntityModelProcessor`: `src/Headless.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`
- `HeadlessDbContext.CoreSaveChangesAsync`: `src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:34`
- `ProcessBeforeSaveReport`: `src/Headless.Orm.EntityFramework/Contexts/ProcessBeforeSaveReport.cs`
- `ICurrentUser` / `ICurrentTenant`: `src/Headless.Core/Abstractions/ICurrentUser.cs`, `ICurrentTenant.cs`
- `IClock`: `src/Headless.Core/Abstractions/IClock.cs`
- DI setup pattern: `src/Headless.Orm.EntityFramework/Setup.cs` (C# 14 `extension` syntax)
- UserId/AccountId primitives: `src/Headless.Extensions/Primitives/UserId.cs` (source-generated, uses `.ToString()`)

### Research

- ABP.io entity history: opt-in via `EntityHistorySelectors`, request-scoped audit log
- Audit.NET: scope-based lifecycle, 20+ storage providers, no built-in sensitive data handling
- EF Core ChangeTracker: `EntityEntry.Properties` gives `OriginalValue`/`CurrentValue`/`IsModified` without extra queries
