---
date: 2026-06-20
topic: blobs-named-storage
---

# Multiple Named Blob Storages in One DI Container

## Summary

Introduce a unified `AddHeadlessBlobs(...)` setup builder that registers one default plus any number of **named** blob stores in a single service collection — including multiple instances of the *same* provider — each with fully isolated configuration, resolved by name through an `IBlobStorageProvider` and keyed DI. This mirrors the existing `Headless.Caching.Core` builder + `ICacheProvider` model in a new `Headless.Blobs.Core` package.

## Problem Frame

Today each blob provider exposes its own `Add{Provider}BlobStorage(this IServiceCollection)` extension that registers a single `IBlobStorage` singleton (e.g. `SetupAwsS3.AddAwsS3BlobStorage` in `src/Headless.Blobs.Aws/Setup.cs`). A second `Add…BlobStorage()` call just appends a duplicate `IBlobStorage` descriptor — last-registration-wins for direct injection, with `IEnumerable<IBlobStorage>` the only way to see both and no way to tell them apart.

Real applications routinely need more than one store at once: user images on a CDN-backed bucket (R2/S3), private documents on Azure, scratch files on the local file system — and frequently two instances of the same provider (a prod bucket and a staging bucket, or two R2 accounts). The framework already solved this exact shape for caching (`HeadlessCachingSetupBuilder` with default + named instance slots, resolved via `ICacheProvider.GetCache(name)`), but blobs has no Core/builder package at all. The cost of the gap is that multi-store apps must hand-roll their own keyed wiring around the provider packages, defeating the point of an unopinionated, batteries-included framework.

## Key Decisions

- **Named coexistence, not coordination.** Stores are independent and addressed explicitly by name. The framework performs no routing-by-container, replication/fan-out, or fallback chaining — those are deliberately out of scope (see Scope Boundaries).
- **Mirror the caching architecture.** A new `Headless.Blobs.Core` package owns `AddHeadlessBlobs(Action<HeadlessBlobsSetupBuilder>)`, the builder, and `IBlobStorageProvider`. Provider packages contribute `Use{Provider}` extension members on the builder. Deferred-contribution + gate-validation + called-once marker semantics are copied from `SetupCachingCore._AddCachingProviderCore`.
- **No "tier" concept.** Caching's role-keyed tiers (memory/remote/hybrid) are caching-specific. Blobs has two builder slots only: **default (optional, at most one)** and **named (unlimited, unique)**.
- **The default store is optional.** Unlike caching (which mandates exactly one default), a blobs configuration may register named stores only, with no default. There is no single implicit "the store" that most blobs code injects, so forcing a default adds friction without value.
- **Unified builder replaces per-provider extensions.** The `Add{Provider}BlobStorage(IServiceCollection)` entry points are removed in favor of the builder. This is a breaking change, consistent with the greenfield/breaking stance.
- **Full per-instance isolation.** Because the same provider can be registered N times, none of options, provider client, or naming normalizer can remain a shared DI singleton. Each store binds its own **named options** (`Configure<TOptions,TValidator>(action, name)`), constructs its own provider client (no shared `TryAddAWSService<IAmazonS3>`), and carries its own `IBlobNamingNormalizer`.
- **Resolution mirrors `ICacheProvider`.** Named stores register as keyed `IBlobStorage` services and resolve through `IBlobStorageProvider.GetStorage(name)` / `GetStorageOrNull(name)`. Both surfaces are provided, exactly as caching exposes keyed `ICache` + `ICacheProvider`.
- **Presigned access becomes per-store.** The current global `IPresignedUrlBlobStorage` singleton alias (`src/Headless.Blobs.Aws/Setup.cs`) is dropped. Presigned support is a capability of each resolved store: a caller resolves a named/default `IBlobStorage` and checks/casts to `IPresignedUrlBlobStorage`.

## Requirements

**Registration API**

- R1. A new `Headless.Blobs.Core` package exposes a single `AddHeadlessBlobs(Action<HeadlessBlobsSetupBuilder> configure)` extension on `IServiceCollection` as the only registration entry point.
- R2. The builder supports registering an optional **default** store (at most one), injectable as a plain (unkeyed) `IBlobStorage`. A named-only configuration with no default is valid.
- R3. The builder supports registering any number of **named** stores under unique names; a duplicate name is a configuration error.
- R4. The same provider may back multiple named stores with independent configuration (e.g. two R2 buckets).
- R5. Each provider package contributes `Use{Provider}` members on the builder for both the default and named-instance paths, following the options overload trio in CLAUDE.md (`Action<TOptions>`, `IConfiguration`, `Action<TOptions, IServiceProvider>`).
- R6. The existing `Add{Provider}BlobStorage(IServiceCollection)` extensions are removed across all six provider packages (Aws, Azure, CloudflareR2, FileSystem, Redis, SshNet).

**Resolution**

- R7. `IBlobStorageProvider` exposes `GetStorage(string name)` (throws when unregistered) and `GetStorageOrNull(string name)` (returns null), mirroring `ICacheProvider`.
- R8. Named stores are resolvable as keyed `IBlobStorage` services (e.g. `[FromKeyedServices("docs")] IBlobStorage`).
- R9. The default store is resolvable as a plain `IBlobStorage`; named stores never register into the default (unkeyed) `IBlobStorage`.

**Per-instance isolation**

- R10. Each store binds its own options instance via named options, validated independently.
- R11. Each store constructs its own provider client (`IAmazonS3`, Azure client, Redis multiplexer, SSH client, etc.) from that store's options; no provider client is shared across stores via a DI singleton.
- R12. Each store carries its own `IBlobNamingNormalizer` appropriate to its provider; the normalizer is no longer a global DI singleton.

**Setup gates**

- R13. `AddHeadlessBlobs` validates its configuration before mutating the service collection: contributions are deferred and applied only after gates pass (mirroring `_AddCachingProviderCore`).
- R14. Calling `AddHeadlessBlobs` more than once on the same service collection is a configuration error (enforced via a marker registration).
- R15. At most one default store may be registered; registering two defaults is a configuration error. Zero defaults (named-only) is valid.

**Capabilities**

- R16. `IPresignedUrlBlobStorage` is no longer registered as a global singleton; presigned support is determined per resolved store (the store instance implements `IPresignedUrlBlobStorage` when its provider supports it).

## Key Flows

- F1. Registration and build
  - **Trigger:** Application calls `AddHeadlessBlobs(b => …)` during DI setup.
  - **Steps:** The builder collects deferred contributions into the default slot and the named slot; on return, the core validates gates (at most one default, unique names, not-already-called); on success it applies the default contribution (if any), then each named contribution, registering keyed `IBlobStorage` services, named options, per-instance clients/normalizers, and `IBlobStorageProvider`.
  - **Outcome:** A service collection with one optional default `IBlobStorage`, N keyed `IBlobStorage` stores, and `IBlobStorageProvider`. A failed gate leaves the collection unchanged.
  - **Covered by:** R1, R2, R3, R10, R11, R12, R13, R14, R15.

- F2. Resolution at runtime
  - **Trigger:** A consumer needs a specific store.
  - **Steps:** Inject the default `IBlobStorage` directly, or call `IBlobStorageProvider.GetStorage("images")`, or inject `[FromKeyedServices("images")] IBlobStorage`; for presigned URLs, check/cast the resolved store to `IPresignedUrlBlobStorage`.
  - **Outcome:** The caller operates against the intended store with its own config and normalizer.
  - **Covered by:** R7, R8, R9, R16.

## Acceptance Examples

- AE1. Default cardinality
  - **Given** a builder with two `Use{Provider}` default calls, **When** `AddHeadlessBlobs` returns, **Then** a configuration error is thrown; **and given** a builder with only named stores and no default, **Then** registration succeeds. **Covers R15.**
- AE2. Duplicate name rejected
  - **Given** two named stores registered under the same name, **When** the builder is configured, **Then** a configuration error is thrown. **Covers R3.**
- AE3. Double registration rejected
  - **Given** `AddHeadlessBlobs` already called on a service collection, **When** it is called again, **Then** a configuration error is thrown. **Covers R14.**
- AE4. No leak into default
  - **Given** only named stores are registered, **When** a consumer resolves a plain `IBlobStorage`, **Then** none of the named stores satisfy that injection. **Covers R9.**
- AE5. Same provider, isolated config
  - **Given** two named R2 stores with different buckets/credentials, **When** each is resolved, **Then** each operates against its own client and options with no shared state. **Covers R4, R10, R11.**
- AE6. Presigned is per-store
  - **Given** one store on a presigned-capable provider and one on a provider without support, **When** each resolved store is checked for `IPresignedUrlBlobStorage`, **Then** only the capable store satisfies the cast. **Covers R16.**

## Scope Boundaries

- Any cross-store coordination is out of scope: routing-by-container/prefix, replication/fan-out writes, primary-with-fallback chains, and cross-store `Copy`/`Rename` (today `CopyAsync`/`RenameAsync` operate within a single store). Named stores are independent; these behaviors could layer on top of named stores in a later iteration.
- No change to the `IBlobStorage` operational contract (upload/download/list/delete/etc.) — only how stores are registered and resolved.

## Dependencies / Assumptions

- The design assumes structural parity with `Headless.Caching.Core` (`SetupCachingCore`, `HeadlessCachingSetupBuilder`, `ICacheProvider`, `KeyedServiceCacheProvider`) as the reference implementation.
- Assumes the per-provider engines accept their client/options/normalizer by constructor (verified for `AwsBlobStorage` at `src/Headless.Blobs.Aws/AwsBlobStorage.cs`) so they can be built per keyed instance via a factory; the same will need confirming for Azure/Redis/FileSystem/SshNet engines during planning.
- Breaking changes to the public registration surface are acceptable per the greenfield stance in CLAUDE.md.
- Per the abstraction+provider testing rule, a `Headless.Blobs.Tests.Harness` already exists; multi-store conformance scenarios extend it rather than copy fixtures.

## Outstanding Questions

**Deferred to planning**

- OQ1. Whether the default store also gets a conventional keyed alias resolvable through `IBlobStorageProvider` (caching aliases its default under a role key), or whether the default is reachable only as the plain `IBlobStorage`.
- OQ2. Exact per-provider client instancing mechanics (how each provider builds its client from named options inside the keyed factory — e.g. wrapping `IOptionsMonitor<T>.Get(name)` as `IOptions<T>` for engines that read `optionsAccessor.Value`).
- OQ3. Migration guidance/changelog for downstream consumers of the removed `Add{Provider}BlobStorage` extensions and the removed global `IPresignedUrlBlobStorage` registration.

## Sources / Research

- `src/Headless.Caching.Core/Setup.cs` — `AddHeadlessCaching` entry, gate validation, deferred-contribution application order, called-once marker.
- `src/Headless.Caching.Core/HeadlessCachingSetupBuilder.cs` — default/tier/named/cross-cutting slots, `AddNamed` validation.
- `src/Headless.Caching.InMemory/Setup.cs` — provider contribution pattern: `Use{Provider}` (default) + named-instance overloads, named options via `Configure(..., name)`, keyed-singleton factory pulling `IOptionsMonitor<T>.Get(name)`.
- `src/Headless.Caching.Abstractions/ICacheProvider.cs` — the `GetCache` / `GetCacheOrNull` resolution surface to mirror.
- `src/Headless.Blobs.Aws/Setup.cs` and `src/Headless.Blobs.Aws/AwsBlobStorage.cs` — current single-store registration, shared `IAmazonS3`/normalizer/options singletons, and global `IPresignedUrlBlobStorage` alias that must become per-instance.
- `src/Headless.Blobs.Abstractions/IBlobStorage.cs`, `IPresignedUrlBlobStorage.cs`, `IBlobNamingNormalizer.cs` — unchanged operational contracts.
