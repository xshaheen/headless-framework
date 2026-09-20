---
title: "Namespace policy: root-namespace anchor, shared family roots, three-tier placement"
date: 2026-09-18
last_updated: 2026-09-18
category: conventions
module: headless-framework
problem_type: naming_convention
component: package_structure
severity: high
tags:
  - namespaces
  - package-structure
  - extension-methods
  - cs0433
related_components:
  - provider_packages
  - registration_surface
applies_when:
  - "Choosing the namespace for a new type, extension holder, or Setup class"
  - "Adding a package to an existing feature family"
  - "Adding an extension method on a BCL or third-party type"
---

# Namespace policy

## The anchor is `<RootNamespace>`, not the package name

A package's entire public surface — types, interfaces, enums, and registration and extension holder classes
(`Add*`, `Use*`, `Map*`) — lives under its root namespace. When the root namespace differs from the package name
(`Headless.EntityFramework.Messaging` ships `Headless.EntityFramework`), declare `<RootNamespace>` explicitly in
the `.csproj` so the identity is intentional rather than accidental.

## Several packages may share one root namespace

This is the BCL pattern, as in the `Microsoft.Extensions.*` family, and it is the norm for feature families
here: every `Headless.Caching.*` package ships `Headless.Caching`, and provider, `.Core`, and `.Abstractions`
packages share their feature root.

One constraint makes it safe: **type names must stay unique within the shared namespace across all sibling
packages.** Two packages that ship a same-name type into one namespace produce CS0433 for consumers.

A namespace that exactly matches another package's name belongs to that package: only `Headless.Api.Abstractions`
may ship the `Headless.Api.Abstractions` namespace. Outside that, family packages share their feature root
freely.

### Documented exception: the API response envelopes

`DataEnvelope<T>`, `CollectionEnvelope<T>`, `ValueEnvelope<T>`, `IdEnvelope`, `IdMessageEnvelope`,
`MessageEnvelope`, `OperationDescriptor`, `OperationsDataEnvelope<T>`, and `OperationsCollectionEnvelope<T>` in
`Headless.Api.Core`, plus the `ApiResult` conversion holders in `Headless.Api.Mvc` and
`Headless.Api.MinimalApi`, deliberately ship into the `Headless.Primitives` namespace, so envelopes surface
beside the result primitives consumers already import. This is safe only while type names stay unique across
every package that ships into `Headless.Primitives`. Check for a collision before adding a type to that
namespace from any package.

## Three-tier placement

**Tier 1 — types.** Options, records, builders, exceptions, interfaces, and enums always live in the
package-owned or family-owned namespace. A type never goes into `Microsoft.*`, `System.*`, `OpenTelemetry.*`,
`Npgsql`, or any other foreign namespace. When a helper file mixes a type with an extension holder, split the
type into its own file.

**Tier 2 — the registration surface.** `Setup*`, `Add*`, `Use*`, `Map*`, and fluent-builder extension holders
live in the family root namespace, so one `using Headless.<Feature>;` exposes the whole registration API: the
`AddHeadless{Feature}` entry, the builder, and every installed provider's `Use{Provider}` members. This covers
the `Setup*` and `Setup*Named` classes and the messaging `*MessageBuilderExtensions` holders. Every other
provider type — options, storage and client implementations, headers, exceptions — stays in the provider's own
namespace; lambda type inference means an options callback needs no extra `using`.

The `Setup*.cs` file usually sits at the provider package root while declaring the family namespace, so prefix
its `namespace` with:

```csharp
#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
```

**Tier 3 — helper extension methods on foreign types.** Extensions that belong in everyday application code —
on `ILogger`, `HttpContext`, `HttpRequest`, `IFormFile`, `IQueryable`, `DbContext`, `ModelBuilder`,
`SqlConnection`, `NpgsqlConnection` — live in the **augmented type's** namespace
(`Microsoft.Extensions.Logging`, `Microsoft.AspNetCore.Http`, `Microsoft.EntityFrameworkCore`,
`Microsoft.Data.SqlClient`, `Npgsql`). They then surface in IntelliSense next to the type they extend and need
no discovery `using Headless.<Feature>;`. Mark the foreign namespace with the same `IDE0130` pragma.

Holder class names in a foreign namespace must be `Headless`-prefixed and unique across every sibling package
that shares that namespace — `HeadlessHttpContextExtensions`, or `Headless.EntityFramework`'s
`HeadlessMigrateDbContextExtensions` in `Microsoft.EntityFrameworkCore`. Pick a name no sibling package
could also plausibly pick. Never use a bare BCL-collision name such as `ServiceCollectionExtensions` or
`CollectionExtensions`; those produce CS0433 for consumers.

**Deliberate exception to tier 3:** a helper whose foreign namespace would be root `System` on a near-universal
type — `object.ToObject<T>` in `Headless.Serializer` — stays in its Headless namespace. Injecting into root
`System` on `object` is pollution, not discoverability.

## Augmentation packages

These packages exist specifically to augment a foreign namespace, and extend it wholesale rather than through
tier-3 helpers alone: `Headless.Extensions`, `Headless.Primitives`, `Headless.Urls`, `Headless.Hosting`,
`Headless.Testing` (plus `Testing.AspNetCore` and `Testing.Testcontainers` where they extend test-library
namespaces), and `Headless.NetTopologySuite`. Compiler polyfills such as `IsExternalInit` in
`System.Runtime.CompilerServices` are also exempt.

The tier-3 naming rule still applies: prefix holders with `Headless` or with the augmented type
(`HeadlessHttpContextExtensions`, `TaskExtensions`), never with a bare BCL-adjacent name.
