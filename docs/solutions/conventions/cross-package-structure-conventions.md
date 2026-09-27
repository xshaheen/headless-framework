---
title: Cross-Package Structure Conventions — Storage Infix, Serializer Seam & Host Integrations
date: 2026-06-21
last_updated: 2026-09-26
category: conventions
module: headless-framework
problem_type: naming_convention
component: package_structure
severity: low
related_components:
  - storage_providers
  - serialization
  - aspnetcore_integrations
tags:
  - naming
  - package-structure
  - storage
  - serialization
  - aspnetcore
---

# Cross-Package Structure Conventions

Three cross-cutting decisions that recur when adding packages. The first two came out of the cross-package
coherence review (findings **N2** and **S2**).

## 1. The `.Storage.<Provider>` infix (N2)

**Rule:** use the `.Storage.` infix **only when the package persists the feature's own domain data**.
Infrastructure backends omit it.

| Shape | Meaning | Examples |
|---|---|---|
| `Headless.<Feature>.Storage.<Provider>` | Persists the feature's **domain entities** | `AuditLog.Storage.PostgreSql`, `Features.Storage.SqlServer`, `Permissions.Storage.EntityFramework`, `Settings.Storage.*`, `Messaging.Storage.*` |
| `Headless.<Feature>.<Provider>` | An **infrastructure backend**, not a domain-data store | `DistributedLocks.PostgreSql` (advisory-lock backend), `Coordination.SqlServer` (membership state), `UnitOfWork.PostgreSql` (transaction resource), `Sql.PostgreSql` (raw SQL access) |

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

## 3. Host integrations are named after the feature they extend

**Rule:** a package that adds an ASP.NET Core surface to a Headless feature family is named
`Headless.<Feature>.<Capability>`, ships into the feature's family namespace, and names the capability the consumer
gets. `Headless.Api.*` is reserved for packages whose subject is the HTTP pipeline itself.

| Shape | Meaning | Examples |
|---|---|---|
| `Headless.<Feature>.<Capability>` | ASP.NET Core surface for a Headless feature family | `Caching.OutputCache`, `Jobs.Dashboard`, `Messaging.Dashboard`, `Blobs.SignedUrlEndpoint` |
| `Headless.Api.<Concern>` | The HTTP pipeline itself, or a third-party library adapted into it | `Api.Core`, `Api.Mvc`, `Api.MinimalApi`, `Api.Idempotency`, `Api.FluentValidation`, `Api.Logging.Serilog` |

**Why feature-first:** a consumer looking for signed blob URLs searches the `Blobs.*` packages, not `Api.*`. An
`Api.<Feature>` name also misstates the product, since `Api.Blobs` reads as a REST API over blob storage, and it
splits the package name from the family namespace the namespace policy anchors on. The package that prompted this
rule shipped briefly as `Headless.Api.Blobs` before it was renamed.

**Name the capability, not the host.** Prefer `.OutputCache`, `.Dashboard`, or `.SignedUrlEndpoint` over
`.AspNetCore`. A host-named package tends to collect every unrelated ASP.NET helper for its feature, and no Headless
feature package uses the suffix. A capability name also tells a reader when the package is unnecessary:
`SignedUrlEndpoint` rather than `SignedUrls`, because S3 and Azure sign URLs without it.

**Known exception:** `Headless.Api.DataProtection` persists ASP.NET Core data-protection keys to blob storage and
adds key-ring health checks. Its subject is ASP.NET Core data protection, which is neither a Headless feature family
nor the request pipeline, so it keeps its name rather than moving under `Blobs`.
