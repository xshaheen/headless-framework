---
domain: Multi-Tenancy
packages:
---

# Entity Framework tenancy

> Part of [Multi-Tenancy](../multi-tenancy.md). Make EF Core entities tenant-owned, turn on the write and read guards, and know which data paths they do not cover. The ownership API and schema rollout are in [ORM](../orm.md#tenant-ownership).

## EF Core Integration

`Headless.EntityFramework` applies tenant ownership after your model configuration. To take part:

- Derive your context from `HeadlessDbContext`.
- Call `base.OnModelCreating(modelBuilder)`.
- Declare `IsTenantOwned()` on the entity builder, or implement `IMultiTenant`.

Queries compare the mapped or shadow tenant property with `ICurrentTenant.Id`. The named filter is `HeadlessQueryFilters.MultiTenancyFilter` (the string `"MultiTenantFilter"`), and your own filters stay. `ExecuteUpdate` and `ExecuteDelete` apply the filter but skip the `SaveChanges` guard and the tenant concurrency predicate. `IgnoreMultiTenancyFilter()` removes the filter from one query.

**Verify:** in a test, insert rows for tenants `a` and `b`, then query inside `currentTenant.Change("a")` and assert only tenant `a` rows return.

## Entity Ownership

Ownership is set on the root entity and drives the query filter, the optional write guard, and the tenant concurrency predicate. [ORM tenant ownership](../orm.md#tenant-ownership) has the API, the supported mappings, and `IsTenantScoped()` for unique indexes.

- **Tenant-owned aggregates** use `IsTenantOwned(propertyName = "TenantId")` or implement `IMultiTenant`. `IsNotTenantOwned()` excludes a root even when it implements the interface. Roots owned only through `IsTenantOwned()` require a tenant. `IMultiTenant` roots keep their own nullability, except under tenant-owned Identity.
- **Platform-level entities** stay unowned: cross-tenant infrastructure (outbox rows, audit events, system schedules), shared catalogs (vendor, product, and lookup tables), and cross-tenant read models. Filtering them per tenant would hide rows from legitimate readers or force every reader to bypass the filter.
- **The entity that defines the tenant**, the row whose `Id` is the tenant id, is a special case. Owning it routes every lookup through `ICurrentTenant`, which usually breaks onboarding, support tooling, and cross-tenant administration. Protect those rows with an admin authorization policy instead, unless you have a specific reason to own them.
- **Tenant id storage.** You configure the tenant column's type, conversion, collation, and database checks in your own model and migrations. Headless picks no collation, adds no check constraint, and never trims or normalizes an id. The write guard compares ids ordinally in memory. Queries, concurrency predicates, unique indexes, and foreign keys use database equality, so make sure distinct ids stay distinct under your collation and conversions.
- **Schema rollout.** No backfill or migration runs automatically. Follow [Tenant schema rollout](../orm.md#tenant-schema-rollout), and review the generated migrations for each database provider you use.
- **Identity** has its own opt-in, `ConfigureTenantOwnedIdentity(modelBuilder)` after the base model call. It scopes normalized user and role names per tenant and enforces same-tenant relationships with composite foreign keys. External-login pairs and passkey credential ids stay global. Tenant ownership of an existing Identity row cannot change, even under the write-guard bypass. See [Identity tenant configuration](../identity.md#tenant-owned-identity).
- **Change tenants with fresh contexts.** The context reads the ambient tenant per query, but the filter cannot hide an entity `FindAsync` already tracks. Use a fresh `DbContext`, and fresh Identity store and manager DI scopes, after a tenant change.

## EF Tenant Write Guard

The write guard is off by default. Turn it on through the tenancy builder:

```csharp
builder.Services.AddHeadlessDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
```

`GuardTenantWrites()` is the only way to turn it on. `TenantGuardOptions.GuardWrites` reports the state and has no public setter. When the seam asked for the guard but the options resolve to off, for example because a later registration replaced `IOptions<TenantGuardOptions>`, startup fails with `HEADLESS_TENANCY_EF_WRITE_GUARD_DISABLED`.

With the guard on, `SaveChanges` checks tenant-owned entries before local domain-event handlers run. If a handler runs, it checks the resulting entries again before anything is written:

- A missing tenant is stamped from `ICurrentTenant.Id` when the entry becomes `Added`, including a state change to `Added`. A supplied tenant is kept. Adding under tenant A and saving under tenant B fails.
- An `Added` entity whose explicit `TenantId` differs from the current tenant fails with `CrossTenantWriteException`.
- A modified, soft-deleted, or deleted entity of another tenant fails with `CrossTenantWriteException`, and so does an existing row whose original and current tenant differ.
- No ambient tenant, or a missing required tenant on an `Added` entry, fails with `MissingTenantContextException` (HTTP 403 `g:tenant_required`).
- Entities that are not tenant-owned pass.

Without the guard, only `IMultiTenant` entities get their tenant stamped at save time. Assign the tenant of a root owned only through `IsTenantOwned()` before you track it. A required Identity tenant key can fail with `MissingTenantContextException` during tracking, before `SaveChanges`.

The tenant concurrency predicate stays on with or without the guard, next to your own concurrency tokens. A detached update or delete with a forged original tenant cannot touch another tenant's row. A zero-row result is `DbUpdateConcurrencyException`, which can happen after local handlers ran. A failed save also prevents the outbox rows from being written, but cannot undo local or external handler effects.

The Headless exception handler maps `CrossTenantWriteException` to 409 Conflict with `g:cross_tenant_write` and logs a warning with event name `CrossTenantWriteException`. The response body carries only the code and title.

`CrossTenantWriteException` is not transient. Never retry it: exclude it from catch-all retry policies such as `Policy.Handle<Exception>()`. A retry fails the same way, or, if the ambient tenant changed between attempts, persists the unsafe write.

For an intentional admin or host-level write, keep the bypass narrow. `AddHeadlessTenancy` and `AddHeadlessDbContextServices()` both register `ITenantWriteGuardBypass`, so it resolves in a host without EF Core too:

```csharp
var bypass = serviceProvider.GetRequiredService<ITenantWriteGuardBypass>();

using (bypass.BeginBypass())
{
    await dbContext.SaveChangesAsync(cancellationToken);
}
```

- `IgnoreMultiTenancyFilter()` bypasses reads only. Loading a row through it does not allow updating or deleting that row under the guard. Wrap only the intended write in `BeginBypass()`.
- `BeginBypass()` keeps SQL tenant predicates, required columns, and database constraints, and does not supply a valid original tenant. It has no effect on raw SQL.

**Verify:** in a test with the guard on, saving a new tenant-owned entity with no current tenant throws `MissingTenantContextException`, and modifying another tenant's row throws `CrossTenantWriteException`.

## EF Tenant Read Guard

The read guard is off by default and independent of the write guard:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites().GuardTenantReads()));
```

Without it, a query over a tenant-owned entity with a required tenant column returns no rows when no tenant is set, so a job that forgot its tenant looks healthy while it reads and bulk-deletes nothing. With `GuardTenantReads()`, such a query throws `MissingTenantContextException` when it runs while `ICurrentTenant.Id` is null or white space. The exception is not wrapped, so it maps to the same 403 as the write guard.

- **Covered:** every query that applies `HeadlessQueryFilters.MultiTenancyFilter` to an entity with a required tenant column: materializing queries, `ExecuteUpdate` and `ExecuteDelete`, `Include`, and explicit navigation loads. The check runs on each execution, so a query compiled under one tenant still checks the current one.
- **Required columns only.** Roots declared with `IsTenantOwned()` get a required column. `IMultiTenant` roots keep their own nullability. An entity with a nullable tenant column keeps returning host rows (tenant null) when no tenant is set. Among framework entities, only tenant-owned Identity is affected. Permission grants, settings, and features keep nullable tenant columns. Under `GuardTenantWrites()`, the EF permission grant repository writes host grants through the bypass, so a host or seed run can still grant to host roles.
- **Bypass:** `IgnoreMultiTenancyFilter()` removes the filter, and with it the guard, for one query. Mark it with `// MULTI-TENANCY-BYPASS: <reason>`. `BeginBypass()` does not relax reads.
- **Not covered:** raw SQL (`FromSql`, `ExecuteSql`) and `FindAsync` hits on already tracked entities. Neither runs the filter.
- **Lookups before tenant resolution.** Claim resolution runs after `UseAuthentication()`. With tenant-owned Identity, a user lookup inside authentication, such as a cookie security-stamp validator calling `FindByIdAsync`, throws instead of finding no user. Resolve the tenant before such a lookup, or run it through your own query that calls `IgnoreMultiTenancyFilter()` on purpose.

`GuardTenantReads()` is the only way to turn it on. `TenantGuardOptions.GuardReads` reports the state and has no public setter. When the seam asked for the guard but the options resolve to off, startup fails with `HEADLESS_TENANCY_EF_READ_GUARD_DISABLED`.

**Verify:** in a test with the guard on and no current tenant, a query over a required-tenant entity throws `MissingTenantContextException`.

## Defense Layers and Known Gaps

| Path | Protection and limit |
|---|---|
| LINQ reads | Tenant query filter. A tracked `FindAsync` match skips the query. |
| `ExecuteUpdate` / `ExecuteDelete` | Query filter only. No `SaveChanges` guard and no tenant concurrency predicate. |
| Tracked and detached `SaveChanges` writes | Optional in-memory guard, plus the tenant concurrency predicate on generated updates and deletes. |
| Tenant-owned Identity relationships | Composite foreign keys keep links within one tenant, including direct SQL writes. |
| Raw SQL commands, stored procedures, Dapper, and other direct commands | No filter and no guard. Existing database constraints still apply. |

Give raw SQL explicit tenant predicates from trusted context, and authorize the operation. `BeginBypass()` gives raw SQL no protection. A filter bypass is not authorization, and SQL constraints are not a complete authorization policy.
