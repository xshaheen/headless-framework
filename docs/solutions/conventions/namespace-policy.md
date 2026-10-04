---
title: "Namespace policy: root-namespace anchor, shared family roots, three-tier placement"
date: 2026-09-18
last_updated: 2026-10-04
module: headless-framework
problem_type: convention
component: package_structure
severity: high
tags: [namespaces, package-structure, extension-methods, cs0433, internal, ide0130]
related_components:
  - provider_packages
  - registration_surface
applies_when:
  - "Choosing the namespace for a new type, extension holder, or Setup class"
  - "Adding a package to an existing feature family"
  - "Adding an extension method on a BCL or third-party type"
  - "Adding a sub-namespace or reorganizing folders"
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

A namespace that exactly matches another package's name belongs to that package, and no other package ships
into it. Outside that, family packages share their feature root freely.

### Documented exception: the API response envelopes

`DataEnvelope<T>`, `CollectionEnvelope<T>`, `ValueEnvelope<T>`, `IdEnvelope`, `IdMessageEnvelope`,
`MessageEnvelope`, `OperationDescriptor`, `OperationsDataEnvelope<T>`, and `OperationsCollectionEnvelope<T>` in
`Headless.Api.Core`, plus the `ApiResult` conversion holders in `Headless.Api.Mvc` and
`Headless.Api.MinimalApi`, deliberately ship into the `Headless.Primitives` namespace, so envelopes surface
beside the result primitives consumers already import. This is safe only while type names stay unique across
every package that ships into `Headless.Primitives`. Check for a collision before adding a type to that
namespace from any package.

## A sub-namespace needs a distinct audience

A family's public types live in its root namespace by default, so a feature's common tasks need one `using`.
Add a sub-namespace only for a distinct audience or opt-in area: a provider (`Headless.DistributedLocks.Redis`),
`Internal`, extension points for implementers (`Headless.Messaging.Transport`), `Testing`, or `Dashboard`.
Never name one after a kind of type (`Models`, `Enums`, `Interfaces`, `Entities`, `Exceptions`, `Helpers`,
`Extensions`, `Constants`, `Dtos`, `Base`, `Utilities`): it groups types by implementation detail instead of
use, so one scenario needs several imports.

Folders organize files, not namespaces. `.editorconfig` sets `dotnet_style_namespace_match_folder = false`, so a
file declares the namespace this policy gives it wherever it sits, with no `IDE0130` suppression.

`make check-layering` fails on a public namespace that contains one of those segments after the family root.
`scripts/namespace-baseline.txt` lists the namespaces that predate the check, and the check also fails on a
listed namespace that no longer exists, so remove its line in the change that folds the namespace.

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

The `Setup*.cs` file usually sits at the provider package root while declaring the family namespace; it needs no
suppression, because folders do not dictate namespaces.

**Tier 3 — helper extension methods on foreign types.** Extensions that belong in everyday application code —
on `ILogger`, `HttpContext`, `HttpRequest`, `IFormFile`, `IQueryable`, `DbContext`, `ModelBuilder`,
`SqlConnection`, `NpgsqlConnection` — live in the **augmented type's** namespace
(`Microsoft.Extensions.Logging`, `Microsoft.AspNetCore.Http`, `Microsoft.EntityFrameworkCore`,
`Microsoft.Data.SqlClient`, `Npgsql`). They then surface in IntelliSense next to the type they extend and need
no discovery `using Headless.<Feature>;`.

Holder class names in a foreign namespace must be `Headless`-prefixed and unique across every sibling package
that shares that namespace — `HeadlessHttpContextExtensions`, or `Headless.EntityFramework`'s
`HeadlessMigrateDbContextExtensions` in `Microsoft.EntityFrameworkCore`. Pick a name no sibling package
could also plausibly pick. Never use a bare BCL-collision name such as `ServiceCollectionExtensions` or
`CollectionExtensions`; those produce CS0433 for consumers.

**Deliberate exception to tier 3:** a helper whose foreign namespace would be root `System` on a near-universal
type — `object.ToObject<T>` in `Headless.Serializer` — stays in its Headless namespace. Injecting into root
`System` on `object` is pollution, not discoverability.

## Implementation helpers live in `Internal`

A package's non-public helpers sit in an `Internal/` folder under the `<RootNamespace>.Internal` namespace
(`Headless.Messaging.Internal`, `Headless.Primitives.Internal`). The name is singular and never `Internals`. It
marks a visibility tier rather than naming a group of types, as `Microsoft.EntityFrameworkCore.Internal` and
`Microsoft.Extensions.*.Internal` do. A folder file name never carries a leading underscore. Name the file after
its holder class.

## Augmentation packages

These packages exist specifically to augment a foreign namespace, and extend it wholesale rather than through
tier-3 helpers alone: `Headless.Extensions`, `Headless.Primitives`, `Headless.Urls`, `Headless.Hosting`,
`Headless.Testing` (plus `Testing.AspNetCore` and `Testing.Testcontainers` where they extend test-library
namespaces), and `Headless.NetTopologySuite`. Compiler polyfills such as `IsExternalInit` in
`System.Runtime.CompilerServices` are also exempt.

The tier-3 naming rule still applies: prefix holders with `Headless` or with the augmented type
(`HeadlessHttpContextExtensions`, `TaskExtensions`), never with a bare BCL-adjacent name.
