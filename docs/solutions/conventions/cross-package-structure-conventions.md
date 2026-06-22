---
title: Cross-Package Structure Conventions — Storage Infix & Serializer Seam
date: 2026-06-21
last_updated: 2026-06-21
category: conventions
module: headless-framework
problem_type: naming_convention
component: package_structure
severity: low
related_components:
  - storage_providers
  - serialization
tags:
  - naming
  - package-structure
  - storage
  - serialization
---

# Cross-Package Structure Conventions

Two cross-cutting decisions that recur when adding packages. Both came out of the cross-package coherence
review (findings **N2** and **S2**).

## 1. The `.Storage.<Provider>` infix (N2)

**Rule:** use the `.Storage.` infix **only when the package persists the feature's own domain data**.
Infrastructure backends omit it.

| Shape | Meaning | Examples |
|---|---|---|
| `Headless.<Feature>.Storage.<Provider>` | Persists the feature's **domain entities** | `AuditLog.Storage.PostgreSql`, `Features.Storage.SqlServer`, `Permissions.Storage.EntityFramework`, `Settings.Storage.*`, `Messaging.Storage.*` |
| `Headless.<Feature>.<Provider>` | An **infrastructure backend**, not a domain-data store | `DistributedLocks.PostgreSql` (advisory-lock backend), `Coordination.SqlServer` (membership state), `CommitCoordination.PostgreSql`, `Sql.PostgreSql` (raw SQL access) |

**Why the difference is intentional:** `DistributedLocks.PostgreSql` does not store *distributed-lock domain
records* the way `AuditLog.Storage.PostgreSql` stores *audit entries* — it uses PostgreSQL advisory locks as a
coordination primitive. The packages are providers of a *mechanism*, not stores of *domain data*. So the absence
of `.Storage.` is meaningful, not drift. Do **not** retrofit `.Storage.` onto coordination/locks/sql packages.

All relational providers use the **`PostgreSql`** spelling (the lone `Postgres` outlier was renamed).

## 2. The serializer abstraction is a swap seam, not a universal funnel (S2)

**Rule:** `ISerializer` (`Headless.Serializer.Abstractions`) is the **pluggable seam for payloads that a
consumer may want to swap** (JSON ↔ MessagePack) — primarily **cache and messaging payloads**. It is **not**
meant to funnel every serialization in the framework.

- **Route through `ISerializer`** when the format is a consumer choice. Example: `Caching.Core` resolves an
  `ISerializer` via a configurable factory, which is why `Headless.Serializer.MessagePack` exists alongside `Json`.
- **Call `System.Text.Json` directly** for **local/internal** serialization where pluggability is not a goal —
  e.g. `Blobs.Abstractions` blob-content helpers and `Settings.Abstractions` value (de)serialization. These
  contract packages depending on `Headless.Serializer.Json` is **intentional**, not a layering leak.

**Anti-pattern:** forcing Blobs/Settings/Features "local JSON" through `ISerializer` just for uniformity — it
adds indirection with no swap benefit. Conversely, hard-coding `System.Text.Json` in a cache/messaging *payload*
path defeats the seam.
