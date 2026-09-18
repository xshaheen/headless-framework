---
title: "When to extract a Headless.<Feature>.Tests.Harness package"
date: 2026-09-18
last_updated: 2026-09-18
category: best-practices
module: headless-framework
problem_type: test_structure
component: test_project
severity: medium
tags:
  - testing
  - conformance
  - testcontainers
  - package-structure
related_components:
  - integration_tests
  - provider_packages
applies_when:
  - "Adding the second or later provider-integration project for one feature"
  - "Several provider integration projects already own near-identical fixtures"
  - "Deciding whether a test belongs in a shared harness or a leaf project"
---

# Tests.Harness extraction

The abstraction-plus-provider pattern (`Headless.<Feature>.Abstractions` + `Headless.<Feature>.<Provider>`)
means every feature has two or more providers: EF Core, PostgreSQL, and SqlServer for storage domains; Redis and
Memory for caching. The same observable behavior must hold for each one. Test that with a
`Headless.<Feature>.Tests.Harness` package. Do not copy fixtures between `<Provider>.Tests.Integration`
projects.

## Extract when

- You are adding the **second or later** provider-integration project for a feature. Extract the harness first,
  then add the new provider against it.
- **Three or more** hand-rolled fixtures with substantial overlap already exist (more than 30 lines of
  copy-pasteable boilerplate). Queue the extraction as a dedicated batch instead of letting the count grow.

## What goes in the harness package

- An abstract `<Feature>FixtureBase<TOptions>` that owns the Testcontainers container lifecycle, host bootstrap
  (DI plus the `setup.Use…` pivot), the initializer, migration, and topology waiters (the
  `WaitForXxxStorageInitializerAsync` pattern), and inter-test cleanup (DDL drop, container reset).
- An abstract `<Feature>ConformanceTests<TFixture>` carrying the cross-provider scenarios: round-trip,
  idempotency, concurrency and contention, error paths, cancellation behavior, and schema-init re-entry.
  `TFixture` satisfies xUnit v3's `IClassFixture<>` / `ICollectionFixture<>` pattern.
- Shared test-data builders and `Faker<T>` instances for the contract's value types.

## What stays in each leaf integration project

- A concrete `<Provider><Feature>Fixture : <Feature>FixtureBase<<Provider>Options>` that wires the specific
  `setup.Use<Provider>(…)` extension, container image, and connection-string materialization.
- Tests that exercise behavior unique to that backend: PostgreSQL `pg_advisory_xact_lock`, SqlServer
  `sp_getapplock`, Redis Lua scripts, EF Core migration-snapshot drift. These intentionally have no sibling in
  another provider, and need no base class, because they are non-portable by construction.

## Existing harnesses to copy the shape from

- [Headless.Blobs.Tests.Harness](../../../tests/Headless.Blobs.Tests.Harness) — blob-backend conformance (S3, Azure, FS, SSH, Redis)
- [Headless.DistributedLocks.Tests.Harness](../../../tests/Headless.DistributedLocks.Tests.Harness) — lock-provider conformance
- [Headless.EntityFramework.Tests.Harness](../../../tests/Headless.EntityFramework.Tests.Harness) — `HeadlessDbContext` runtime and EF Core base behavior
- [Headless.Messaging.Core.Tests.Harness](../../../tests/Headless.Messaging.Core.Tests.Harness) — messaging dispatch and outbox
- [Headless.Jobs.EntityFramework.Tests.Harness](../../../tests/Headless.Jobs.EntityFramework.Tests.Harness) — Jobs and Coordination conformance across the EF database providers (PostgreSQL, SqlServer). This one uses an interface plus extensions (`IJobsCoordinationFixture`) instead of an abstract fixture base.

## The shape this rule prevents

The storage-domain integration tests
(`Headless.{AuditLog,Features,Permissions,Settings}.Storage.{EntityFramework,PostgreSql,SqlServer}.Tests.Integration`)
each own a private `<Provider><Feature>Fixture.cs` with substantial overlap. When you add a new domain or
provider there, extract first.
