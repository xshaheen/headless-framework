---
title: Tenant data placement (schema or database per tenant) — evaluated and not adopted
date: 2026-09-30
module: Headless.MultiTenancy
problem_type: tooling_decision
component: tooling
severity: medium
applies_when:
  - A consumer asks for schema-per-tenant or database-per-tenant data isolation
  - Evaluating whether to add per-tenant connection routing to Headless.EntityFramework
  - Reviewing why the tenancy guide states that one shared database is the only supported topology
tags: [multi-tenancy, data-placement, schema-per-tenant, database-per-tenant, entity-framework, outbox, tooling-decision]
related_components: [Headless.MultiTenancy, Headless.EntityFramework, Headless.Messaging, Headless.Jobs, Headless.Api]
---

# Tenant data placement — evaluated and not adopted

## Context

Headless tenancy keeps every tenant's rows in one database and one schema, separated by an ambient
canonical tenant id that drives the EF query filter, the write guard, caches, jobs, and messages.
Pull requests 1019 and 1030 proposed the missing topologies: an EF context registered as
*tenant-routed* would place each tenant in its own schema, its own database, or both, resolved per
tenant and data store by a placement resolver. The implementation was complete and its tests
passed. Both pull requests were closed without merging and the work was discarded.

This document records why, what the feature would have cost, what the rest of the field does, and
what a future implementation must solve before it belongs in the framework.

## Guidance

**Keep one shared database with a tenant column as the only supported topology.** Every project
that consumes the framework uses it, and it is what Microsoft, AWS, PlanetScale, and every library
surveyed recommend for most products.

When a consumer asks for isolation:

1. Ask what the isolation must deliver. Per-tenant backup, restore, and offboarding are usually
   the real need, and a per-tenant export from the shared database covers them.
2. If a customer contract or a regulator requires a separate schema or database, build it as
   opt-in packages behind one generic entry-point hook, not into the core packages. The reason is
   in the next section.
3. Treat database-per-tenant as unsupported until the relay design below exists.
   Schema-per-tenant is the topology to offer first.

## Why this matters

### What the feature would have cost when unused

Measured on the proposed implementation against `main`:

| Cost | Size | Effect on a project that never uses it |
|---|---|---|
| Source packages touched | 13 (45 files, 2 326 lines) | Hooks in the context runtime, three entry points, and six storage packages |
| Placement-only source | 1 771 lines | Resolver, cache, preloader, pin, interceptor, model cache key, migration rewriter |
| Test projects touched | 12 (35 files, 3 571 lines) | Every author of those paths reads the placement rules |
| Runtime work | 2 service lookups that return null | One per context construction, one per tenant entry point |
| EF version risk | Real | The migration rewriter derived from an internal EF class and could break on an EF major release |

The runtime cost was near zero. The maintenance cost was not: every future change to the
`HeadlessDbContext` runtime, to the HTTP, messaging, and Jobs tenancy entry points, or to a relay
store would have had to keep the placement invariants, and one integration test guarded an EF
internal.

### The design that was proposed

Recorded so a future implementation does not start from nothing:

- A resolver keyed by tenant id and data store returns a schema, a connection string, both, an
  explicit shared marker, or nothing. Nothing means the tenant is refused, never sent to the
  shared database by accident. The explicit shared marker exists so a hybrid fleet does not repeat
  the shared connection string per tenant.
- A routed context resolves its placement before construction, because EF builds the model in the
  constructor and the model carries the schema. The factory resolves it asynchronously into a new
  scope, and the tenancy entry points preload it so a context can be injected.
- The context pins its tenant and placement for its whole life and rechecks the ambient tenant on
  every connection open and every command through a singleton interceptor.
- Migrations are scaffolded once against the context's default schema and rewritten per tenant
  schema in the operations, the target model, and the snapshot, with a migrations history table
  per schema.
- All routed context types of a host share one bounded model cache sized by the sum of their
  per-type budgets. One cache per type costs one EF internal service provider per routed context,
  and EF throws past twenty per process.
- Relay-bearing contexts, the outbox storages, the Jobs store, and the catalog, Settings,
  Features, and Permissions stores, refuse a routed context at startup.
- Placements are cached in process only, because they carry connection strings, with an
  invalidator per tenant.

### The gap no option closed

A database-per-tenant deployment has no transactional outbox and no enlisted Jobs writes, because
the relays read one fixed database and `RelationalDatabaseIdentity` refuses enlistment from a unit
on another database. ABP has the same hole and has stated it will not poll thousands of tenant
databases (abpframework/abp issue 10036). Wolverine closes it with a durability agent per tenant
database that routes each envelope to the tenant's store, discovers new tenant databases lazily,
and isolates a failing tenant database from the rest. A future implementation needs that
relay-per-placement design before database-per-tenant is a supported topology. Schema-per-tenant
is unaffected because it shares the relay's database.

### What the field does

- **Finbuckle.MultiTenant** removed the connection string from its tenant model in v7 and has no
  schema-per-tenant mode; its most-pinned issue asks for one. The tenant is captured at
  `DbContext` construction with no stale-tenant guard.
- **ABP** stores connection strings on the tenant entity, resolves one on every context
  acquisition, falls back to the host database when a tenant has none, and has no stale-tenant
  guard.
- **Marten and Wolverine** support database-per-tenant with an unknown-tenant exception, one call
  to migrate every tenant database, and message storage per tenant database.
- **Rails apartment v4** rebuilt on a pool per tenant with a connection budget, an idle reaper,
  and an explicitly deferred "tenant moved to another database" hazard.
- **Laravel tenancy** drives provisioning from tenant lifecycle events through a queued pipeline
  and scopes cache, storage, queue, and session per tenant through bootstrappers.
- **django-tenants** and **apartment** derive the schema name from the tenant key, so a uniform
  topology needs no per-tenant configuration.
- **Hibernate 8** generates row-level-security policies from the tenant mapping and sets a
  transaction-local tenant on every connection.

Two field lessons hold regardless of the decision. Schema-per-tenant cost is driven by catalog
size, not schema count: PlanetScale, Citus, and django-tenants converge on a few thousand schemas
per cluster before planning and migrations degrade. And `search_path` on a shared pool is unsafe
behind PgBouncer transaction mode before PostgreSQL 18 with PgBouncer 1.26, which is why the
proposed design rewrote the EF model per schema instead of setting a search path.

## When to revisit

Revisit when one of these is true:

- A signed customer or a regulator requires a separate schema or database within the next year.
- The framework targets a product line where premium tenants are sold a silo tier.

## What to do instead

Three ideas from the same research pay off for the shared-database topology:

- Tenant id on logs, traces, and metrics at every tenancy entry point (#1035).
- A tenancy seam that scopes blob storage paths and application cache keys by tenant, fail-closed
  with an explicit host bypass (#1036).
- Tenant lifecycle events from the catalog, so provisioning of permissions, settings, and seed
  data can react to a created or deleted tenant. Not started.

## Related

- [docs/llms/multi-tenancy.md](../../llms/multi-tenancy.md), the consumer contract that states the
  shared-database scope.
- [docs/llms/unit-of-work.md](../../llms/unit-of-work.md), the same-database rule that refuses
  enlistment across databases.
- Pull requests 1019 and 1030, closed without merging, with the review discussion.
