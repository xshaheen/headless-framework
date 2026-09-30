---
title: Tenant data placement (schema or database per tenant) — built, evaluated, and dropped from the core
date: 2026-09-30
category: tooling-decisions
module: MultiTenancy
problem_type: tooling_decision
component: tooling
severity: medium
applies_when:
  - A consumer asks for schema-per-tenant or database-per-tenant data isolation
  - Evaluating whether to add per-tenant connection routing to Headless.EntityFramework
  - Reviewing why the tenancy guide states that one shared database is the only supported topology
  - Deciding how to keep a large, tested, but unused feature without paying its maintenance cost
tags: [multi-tenancy, data-placement, schema-per-tenant, database-per-tenant, entity-framework, outbox, tooling-decision]
related_components: [Headless.MultiTenancy, Headless.EntityFramework, Headless.Messaging.Core, Headless.Jobs.Core, Headless.Api.Core]
---

# Tenant data placement — built, evaluated, and dropped from the core

## Context

Headless tenancy keeps every tenant's rows in one database and one schema, separated by an ambient
canonical tenant id that drives the EF query filter, the write guard, caches, jobs, and messages.
Pull requests 1019 and 1030 added the missing topologies: an EF context registered as
*tenant-routed* could place each tenant in its own schema, its own database, or both, resolved per
tenant and data store by an `ITenantDataPlacementResolver`. The work is complete and tested on the
branches `shaheen/feat/tenant-data-placement` (last commit `0e4a53f39`) and
`shaheen/feat/tenant-placement-preload` (last commit `340b3f154`), and the pull requests are closed
without merging.

This document records why, what the feature cost, what the rest of the field does, and the
conditions under which it is worth reviving. The decision page at
[docs/pages/tenant-data-placement-decision.html](../../pages/tenant-data-placement-decision.html)
carries the same material with the options side by side.

## Guidance

**Keep one shared database with a tenant column as the only supported topology.** Every project
that consumes the framework today uses it, and it is what Microsoft, AWS, PlanetScale, and every
library surveyed recommend for most products.

When a consumer asks for isolation:

1. Ask what the isolation must deliver. Per-tenant backup, restore, and offboarding are usually
   the real need, and a per-tenant export from the shared database covers them.
2. If a customer contract or a regulator requires a separate schema or database, revive the
   branches rather than starting over. The hard parts are solved there: migrations scaffolded once
   and rewritten per schema, a context pinned to its tenant with a check on every command, a
   bounded per-schema model cache, and relay stores that refuse a routed context at startup.
3. Revive it as two opt-in packages behind one generic entry-point hook, not into the core. The
   reason is in the next section.

## Why this matters

### What the feature cost when unused

Measured against `origin/main` from the 1030 branch after the follow-up commits:

| Cost | Size | Effect on a project that never uses it |
|---|---|---|
| Source packages touched | 13 (45 files, 2 326 lines) | Hooks in the context runtime, three entry points, and six storage packages |
| Placement-only source | 1 771 lines | Resolver, cache, preloader, pin, interceptor, model cache key, migration rewriter |
| Test projects touched | 12 (35 files, 3 571 lines) | Every author of those paths reads the placement rules |
| Runtime work | 2 service lookups that return null | One per context construction, one per tenant entry point |
| EF version risk | Real | The migration rewriter derives from an internal EF class and can break on an EF major release |

The runtime cost is near zero. The maintenance cost is not: every future change to the
`HeadlessDbContext` runtime, to the HTTP, messaging, and Jobs tenancy entry points, or to a relay
store must keep the placement invariants, and one integration test guards an EF internal.

### The gap no option closes

A database-per-tenant deployment has no transactional outbox and no enlisted Jobs writes, because
the relays read one fixed database and `RelationalDatabaseIdentity` refuses enlistment from a unit
on another database. ABP has the same hole and has stated it will not poll thousands of tenant
databases (abpframework/abp issue 10036). Wolverine closes it with a durability agent per tenant
database. A revived feature needs that relay-per-placement design before database-per-tenant is a
supported topology; schema-per-tenant is unaffected because it shares the relay's database.

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
branches rewrite the EF model per schema instead of setting a search path.

## When to revisit

Revive the branches when one of these is true:

- A signed customer or a regulator requires a separate schema or database within the next year.
- The framework targets a product line where premium tenants are sold a silo tier.

Delete the branches when the framework is scoped to products where one shared database is the
whole story for the foreseeable future. Keep this document either way.

## What to do instead

Three ideas from the same research pay off for the shared-database topology and are in progress
on their own branches:

- Tenant id on logs, traces, and metrics at every tenancy entry point
  (`shaheen/feat/tenant-telemetry`).
- A tenancy seam that scopes blob storage paths and application cache keys by tenant, fail-closed
  with an explicit host bypass (`shaheen/feat/tenant-scoped-storage`).
- Tenant lifecycle events from the catalog, so provisioning of permissions, settings, and seed
  data can react to a created or deleted tenant. Not started.

## Related

- [docs/llms/multi-tenancy.md](../../llms/multi-tenancy.md), the consumer contract that states the
  shared-database scope.
- [docs/llms/unit-of-work.md](../../llms/unit-of-work.md), the same-database rule that refuses
  enlistment across databases.
- Pull requests 1019 and 1030, closed without merging, with the review discussion.
