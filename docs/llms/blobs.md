---
domain: Blob Storage
packages: Blobs.Abstractions, Blobs.Core, Blobs.Aws, Blobs.Azure, Blobs.CloudflareR2, Blobs.FileSystem, Blobs.Redis, Blobs.SshNet
---

# Blob Storage

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Addressing: BlobLocation and the resolve seam](#addressing-bloblocation-and-the-resolve-seam)
    - [Listing: token paging and guarantee tiers](#listing-token-paging-and-guarantee-tiers)
    - [Prefix vs. glob filtering](#prefix-vs-glob-filtering)
    - [Capabilities: container management and presigned URLs](#capabilities-container-management-and-presigned-urls)
    - [Metadata and sidecar companions](#metadata-and-sidecar-companions)
    - [Move, copy, and bulk results](#move-copy-and-bulk-results)
    - [Migration from the array-addressing contract](#migration-from-the-array-addressing-contract)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Blobs.Abstractions](#headlessblobsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Blobs.Core](#headlessblobscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Blobs.Aws](#headlessblobsaws)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
        - [appsettings.json](#appsettingsjson)
        - [Options](#options)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Blobs.Azure](#headlessblobsazure)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
        - [appsettings.json](#appsettingsjson-1)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Blobs.CloudflareR2](#headlessblobscloudflarer2)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
        - [appsettings.json](#appsettingsjson-2)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Blobs.FileSystem](#headlessblobsfilesystem)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
        - [appsettings.json](#appsettingsjson-3)
        - [Options](#options-1)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Blobs.Redis](#headlessblobsredis)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)
- [Headless.Blobs.SshNet](#headlessblobssshnet)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-7)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-7)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
        - [appsettings.json](#appsettingsjson-4)
        - [SSH Key Authentication](#ssh-key-authentication)
    - [Dependencies](#dependencies-7)
    - [Side Effects](#side-effects-7)

> Provider-agnostic file/blob storage with implementations for AWS S3, Azure Blob, Cloudflare R2, local filesystem, Redis, and SFTP.

## Quick Orientation

Install `Headless.Blobs.Abstractions`, `Headless.Blobs.Core`, and one or more provider packages. Register every store through a single `AddHeadlessBlobs(...)` call: pick a default with `Use{Provider}(...)` and add named stores with `AddNamed(name, i => i.Use{Provider}(...))`. Code against `IBlobStorage` — never reference concrete provider types in application code.

Provider selection guide:
- **Development/testing**: `Headless.Blobs.FileSystem` — no external dependencies, stores files on disk.
- **Production (AWS)**: `Headless.Blobs.Aws` — full S3 integration with bulk operations and presigned URLs.
- **Production (Azure)**: `Headless.Blobs.Azure` — Azure Blob Storage with Batch API and Azure.Identity support.
- **Production (Cloudflare R2)**: `Headless.Blobs.CloudflareR2` — private, S3-compatible storage on the reused AWS engine; a cost-saving S3 replacement.
- **SFTP/legacy**: `Headless.Blobs.SshNet` — SFTP protocol for remote servers and legacy system integration.
- **Small cached blobs**: `Headless.Blobs.Redis` — Redis-backed storage for small, ephemeral blobs only (default 10 MB limit).

The default store registers as a plain (unkeyed) `IBlobStorage` singleton; named stores register as keyed `IBlobStorage` singletons and resolve through `IBlobStorageProvider`. Every operation addresses a blob with a single `BlobLocation(container, path)` value — a validated `(top-level container, container-relative object key)` pair — instead of the old `string[] container` + `blobName` shape. Container/bucket lifecycle and presigned URLs are opt-in capabilities (`IBlobContainerManager`, `IPresignedUrlBlobStorage`), not part of the data-plane `IBlobStorage` contract.

## Agent Instructions

- Always depend on `IBlobStorage` from `Headless.Blobs.Abstractions` — never reference `AwsBlobStorage`, `AzureBlobStorage`, or other concrete types in service code.
- Register all stores through `AddHeadlessBlobs(...)` from `Headless.Blobs.Core`. Choose a default with `Use{Provider}(...)` and add named stores with `AddNamed(name, i => i.Use{Provider}(...))`. Use `UseFileSystem` for local development and testing; `UseAws`, `UseAzure`, or `UseCloudflareR2` for production.
- Address every blob with `BlobLocation(container, path)`. The constructor validates path security (traversal, control characters, absolute paths, and any path segment ending in the reserved sidecar suffix) once, so pass the value through — do not pre-normalize. The `params ReadOnlySpan<string>` convenience constructor joins segments with `/`: `new BlobLocation("uploads", "images", fileName)` is the key `images/<fileName>` in container `uploads`. `Move`/`Copy` take a source and a destination `BlobLocation`.
- Normalization is two-tier and applied by the **provider's** resolve step, not by you: `BlobLocation.Container` is the backend bucket/container/root (strict backend rules — lowercase, length, allowed characters) and `BlobLocation.Path` is the lenient object key (validated, not rewritten). The value type validates security; the provider's `IBlobNamingNormalizer` applies backend naming when it resolves the location.
- `UploadAsync` does **not** create a missing top-level container — that is an error. Provision the container first via `IBlobContainerManager.EnsureContainerAsync` or out-of-band (IaC). Filesystem-like providers (FileSystem, SFTP) still create the intermediate path directories inherent to writing a blob.
- Container lifecycle (Ensure/Exists/Delete) lives on `IBlobContainerManager`, **resolved from DI** (`sp.GetService<IBlobContainerManager>()` or `sp.GetKeyedService<IBlobContainerManager>("name")`) — NOT an `is`-cast from the store. AWS, Azure, FileSystem, Redis, and SSH register one; CloudflareR2 does **not** (its object-scoped tokens cannot manage buckets), so resolution returns `null` for an R2 store — null-check the result.
- Presigned URLs are a separate capability discovered with an `is`-cast from the store (`storage is IPresignedUrlBlobStorage presigned`) and take a `BlobLocation`. AWS, Azure, and CloudflareR2 support it; for **named** stores they also register a keyed `IPresignedUrlBlobStorage` (resolve via `[FromKeyedServices("name")]`). FileSystem, Redis, and SshNet are never presigned-capable. There is no global (unkeyed) `IPresignedUrlBlobStorage` registration — do not inject it without a key.
- List with `ListAsync(BlobQuery) → BlobPage(Items, ContinuationToken?)`. A `null` token marks the last page; otherwise round-trip the opaque token into a new `BlobQuery` to fetch the next page. Prefer the `GetBlobsAsync(BlobQuery)` streaming extension (an `IAsyncEnumerable<BlobInfo>`) for full enumeration, or `GetBlobsListAsync(query, limit)` to materialize. FileSystem listing is bounded to O(pageSize) memory per call (sliding window), like SFTP.
- `BlobQuery.Prefix` is the only explicit filter pushed to the backend (and it is validated through the same seam as `BlobLocation`, so a `../` prefix can never reach enumeration). Glob (`*`, `?`) is a **client-side** extension layered over streaming: `GetBlobsAsync(query, globPattern)` derives a compatible literal prefix to narrow enumeration when safe, then applies the matcher client-side. `DeleteAllAsync(BlobQuery)` deletes by validated prefix; glob-delete is list + filter + bulk-delete.
- Pagination stability differs by backend and is documented as explicit tiers: server-side stable on S3/Azure (native continuation tokens); emulated re-scan on FileSystem/SSH (lexicographic start-after-key) and Redis (`HSCAN` cursor) — weaker stability under concurrent writes, same cost as the previous implementation. Treat the token as opaque: never parse, compare, or persist it across provider changes.
- All six providers support metadata, typed as `IReadOnlyDictionary<string, string>?` with non-null values, consistently on the `UploadAsync` parameter, `BlobInfo.Metadata`, and `BlobDownloadResult.Metadata`. FileSystem and SFTP store it in a **sidecar companion file** (reserved `.hlmeta` suffix) beside the blob; sidecars are filtered from every listing/exists/delete result, and any blob key segment ending in the reserved suffix is rejected at `BlobLocation` construction. Listings **omit per-object metadata by default** (`BlobInfo.Metadata` is `null` for every provider); set `BlobQuery.IncludeMetadata = true` to populate it — cheap on Azure/Redis, a per-object HEAD (S3) or sidecar read (FileSystem/SFTP) otherwise. `GetBlobInfoAsync` is the authoritative single-blob source. `BlobDownloadResult.FileName` is the full container-relative key on every provider.
- `MoveAsync(source, destination)` **rejects an occupied destination**: if the destination already exists it returns `false` without touching either blob (use delete-then-move, or `CopyAsync` for overwrite semantics). The pre-check is **non-atomic except on Redis** (whose move is one atomic Lua script) — a destination created concurrently between the check and the copy may still be overwritten, so a hard no-overwrite guarantee needs caller-side serialization. Otherwise it is non-atomic copy-then-delete with a best-effort rollback (delete the destination copy) if the source delete fails. A resolved self-move is a no-op returning `true`. `CopyAsync` leaves the source intact. On FileSystem/SSH the sidecar moves with the blob.
- Bulk operations return `IReadOnlyList<BlobBulkResult>` — each item carries the raw `Container` + `Path`, an optional validated `Location`, and a `Result<bool, Exception>` (`Ok(true)` success, `Ok(false)` not-found for delete, `Fail(ex)` on error). Invalid per-entry paths return `Location: null` but still preserve raw identity. Correlate by identity, not position; a per-entry failure does not abort the batch.
- Always dispose the result of `OpenReadStreamAsync()` (a `BlobDownloadResult`) promptly with `await using` — holding it may exhaust connection pools or SFTP pool slots.
- Non-seekable upload streams are handled per-provider, not uniformly: AWS/R2 and Redis buffer to memory (S3 needs a known length; Redis is capped); Azure, FileSystem, and SFTP stream through. Seekable streams are always rewound to position 0 first.
- Redis blob storage (`Blobs.Redis`) is for small blobs only (metadata, thumbnails, temporary uploads); the default `MaxBlobSizeBytes` is 10 MB. For large files use S3 or Azure. The `UseRedis(IConfiguration)` overload cannot bind the required `IConnectionMultiplexer` property — use the `Action<RedisBlobStorageOptions>` overload to set it.
- A default store is optional and there is at most one (injected as plain `IBlobStorage`); a named-only configuration is valid and leaves plain `IBlobStorage` unregistered. The same provider may back multiple named stores with isolated config. Resolve named stores with `IBlobStorageProvider.GetStorage("name")` or `[FromKeyedServices("name")] IBlobStorage`. Calling `AddHeadlessBlobs` more than once on the same service collection throws.

## Core Concepts

### Addressing: BlobLocation and the resolve seam

Every data-plane operation identifies a blob by a single `BlobLocation` — a `readonly record struct` carrying a top-level `Container` (the provider root: S3 bucket, Azure container, FS/SFTP root, Redis key prefix) and a container-relative `Path` (the object key, which may contain `/` separators). Its constructor validates provider-agnostic path security — traversal sequences, absolute paths, control characters, and the reserved sidecar suffix — exactly once, so every method that accepts a `BlobLocation` is guarded before it reaches a provider. A `params ReadOnlySpan<string>` convenience constructor joins hierarchical segments with `/`: `new BlobLocation("uploads", "images", "a.png")` is the key `images/a.png`.

This replaces the previous `string[] container` + separate `blobName` pair, where the first array element was a "magic" backend root under strict naming rules and the rest formed a lenient key. That two-tier model was never a type; it was re-documented per method and re-implemented per call site, and the spots that skipped the shared helper grew real bugs (un-normalized bulk-delete buckets, filesystem path traversal, SFTP directory mismatch). Folding the address into one validated value type makes that bug class impossible by construction.

`BlobLocation` validates but does **not** normalize, because normalization rules differ per backend. Each provider funnels every operation through one resolve seam (`BlobLocationResolver` + the provider's `IBlobNamingNormalizer`) that re-validates and applies the two-tier normalization (`Container` strict, `Path` lenient). `BlobQuery` (listing/delete) validates its `Container` and `Prefix` through the same seam, so a `../` prefix can never reach directory enumeration on filesystem-like backends.

### Listing: token paging and guarantee tiers

`ListAsync(BlobQuery) → BlobPage(Items, ContinuationToken?)` is the listing primitive. A `null` `ContinuationToken` marks the last page; otherwise the caller round-trips the token into a new `BlobQuery` for the next page. `GetBlobsAsync(BlobQuery)` (an `IAsyncEnumerable<BlobInfo>` extension) and `GetBlobsListAsync(query, limit)` (a materializer) are built on top of it — streaming is sugar over the token primitive.

The token is a **serializable opaque string** that survives a web-request boundary (unlike the previous closure-backed cursor). It is provider-specific; callers must not parse, compare, or persist it across a provider change. Stability is documented as explicit tiers:

| Tier | Providers | Token encoding | Stability |
| --- | --- | --- | --- |
| Server-side stable | AWS S3, Cloudflare R2, Azure | native continuation token | stable across concurrent writes |
| Emulated re-scan | FileSystem, SFTP | lexicographic start-after-key | weaker stability under concurrent writes; same cost as the previous implementation |
| Cursor scan | Redis | native `HSCAN` cursor | non-lexicographic order; may surface duplicates across a rehash — callers must tolerate |

### Prefix vs. glob filtering

`BlobQuery.Prefix` is the only explicit filter pushed down to the backend, and it is path-security validated at construction. Glob matching (`*`, `?`) is an explicit **client-side** filter through one shared matcher, layered over streaming: `GetBlobsAsync(query, globPattern)`. The extension derives a wildcard-free literal prefix and, when that prefix is compatible with the query and there is no caller-supplied continuation token, uses it to narrow the listing before applying the matcher. This ends the per-provider regex divergence and is honest about which filtering the server does (prefix) versus the client (glob). `DeleteAllAsync(BlobQuery)` deletes every blob matched by the validated prefix and returns the count; a glob-scoped delete is list + filter + bulk-delete in the consumer.

### Capabilities: container management and presigned URLs

Two opt-in capabilities live off the `IBlobStorage` data plane, and they are discovered differently — the difference is deliberate:

- **Presigned URLs (`IPresignedUrlBlobStorage`)** are discovered with an `is`-cast from the resolved store (`storage is IPresignedUrlBlobStorage presigned`). The cast stays honest because both AWS and Cloudflare R2 (which reuses the AWS storage type) support signing — the capability tracks the storage type exactly.
- **Container management (`IBlobContainerManager`)** is a **separately registered DI service**, resolved with `GetService`/`GetKeyedService<IBlobContainerManager>(name)`, *not* an `is`-cast. The reason: R2 reuses `AwsBlobStorage` but cannot create buckets (object-scoped tokens), so an `is`-cast from the shared storage type would lie. By registering the manager as its own service, the AWS provider registers one while R2 registers none — and `GetKeyedService<IBlobContainerManager>` honestly returns `null` for an R2 store. AWS, Azure, FileSystem, Redis, and SSH register a manager; R2 does not.

`IBlobContainerManager` exposes `EnsureContainerAsync` (idempotent create), `ContainerExistsAsync`, and `DeleteContainerAsync`. `UploadAsync` no longer auto-creates a missing top-level container — a missing managed container/bucket is an error; provision it through `EnsureContainerAsync` or IaC first. (FileSystem/SSH still create the intermediate *path* directories required to write a blob, which is path creation, not container management.)

### Metadata and sidecar companions

Metadata uses one read-only dictionary shape — `IReadOnlyDictionary<string, string>?` with non-null values — across the `UploadAsync` parameter, `BlobInfo.Metadata`, and `BlobDownloadResult.Metadata`. All six providers round-trip it. S3, Azure, and Redis store it natively (Redis in a separate info hash, atomic via Lua). FileSystem and SFTP, which have no native blob-metadata concept, store it in a **sidecar companion file** beside each blob, named with the reserved `.hlmeta` suffix.

Sidecar trade-offs the agent must know: write order is content-first then sidecar, and a missing sidecar reads as empty metadata, so reads stay safe across a crash window — but the pair is **non-atomic** on FileSystem/SFTP (no transaction). Sidecars are filtered from every listing, existence, count, and delete-all result, so they never surface as blobs or match a prefix/glob. Deleting a blob removes its sidecar, so re-uploading the same key without metadata cannot resurrect stale metadata. Any blob key segment that would collide with the reserved `.hlmeta` form is rejected at `BlobLocation` construction.

**Listings omit metadata by default.** Across every provider, `ListAsync`/`GetBlobsAsync` return `BlobInfo.Metadata == null` unless the query opts in with `BlobQuery.IncludeMetadata = true` — matching the industry norm (jclouds `withDetails`, MinIO `WithMetadata`, Azure `BlobTraits.Metadata`), because metadata-in-listing is not free everywhere. When opted in: Azure requests the metadata trait and Redis reads its info hash (one round-trip, cheap); FileSystem and SFTP read one sidecar per returned page entry; S3 issues one `HeadObject` per returned key (parallelized to `MaxBulkParallelism`). For authoritative single-blob metadata use `GetBlobInfoAsync`.

### Move, copy, and bulk results

Object stores have no native rename, so `MoveAsync(source, destination)` is a copy-then-delete. It **rejects an occupied destination**: if the destination already exists the move returns `false` without touching either blob (do delete-then-move, or use `CopyAsync` for overwrite semantics) — the industry norm, where overwrite-protection is a precondition (`IfNotExist`/`If-None-Match`) rather than a capture-and-restore. This pre-check is **non-atomic on every provider except Redis** (whose move is a single atomic Lua script): a destination created concurrently between the check and the copy can still be overwritten, so a hard no-overwrite guarantee requires the caller to serialize moves to a key (true conditional-copy/`If-None-Match` promotion is a tracked follow-up). Move is otherwise non-atomic: if the source delete fails after a successful copy, the implementation makes a best-effort attempt to roll back by deleting the destination copy so the original is preserved (safe in the non-racing case, since that copy is the one the move just created). A resolved self-move (source and destination resolving to the same backend address) is a no-op returning `true`. `CopyAsync(source, destination)` leaves the source intact and may overwrite. Both take source and destination `BlobLocation` values and may cross containers within a single store. On FileSystem/SFTP the metadata sidecar moves with the blob.

Bulk operations return `IReadOnlyList<BlobBulkResult>`, where each item pairs the raw input `Container` + `Path` with a `Result<bool, Exception>` (`Headless.Primitives`), so results are correlated by identity rather than by position. `Location` is populated only when the input successfully formed a validated `BlobLocation`; invalid per-entry paths still return their raw identity with `Location: null`. For `BulkUploadAsync`: `Ok(true)` on success, `Fail(ex)` on failure. For `BulkDeleteAsync`: `Ok(true)` deleted, `Ok(false)` not found, `Fail(ex)` on failure. A per-entry failure does not abort the batch. `DeleteAllAsync(BlobQuery)` returns a deleted count only when the whole prefix delete succeeds; per-entry/provider failures throw after any partial progress is logged.

### Migration from the array-addressing contract

The operational contract changed wholesale (greenfield, no compatibility layer). Map call sites as follows:

| Old (`string[] container` + `blobName`) | New (`BlobLocation` contract) |
| --- | --- |
| `UploadAsync(container, blobName, stream, metadata, ct)` | `UploadAsync(new BlobLocation(container, path), stream, metadata, ct)` |
| `OpenReadStreamAsync(container, blobName, ct)` | `OpenReadStreamAsync(new BlobLocation(container, path), ct)` |
| `GetBlobInfoAsync(container, blobName, ct)` | `GetBlobInfoAsync(new BlobLocation(container, path), ct)` |
| `ExistsAsync(container, blobName, ct)` | `ExistsAsync(new BlobLocation(container, path), ct)` |
| `DeleteAsync(container, blobName, ct)` | `DeleteAsync(new BlobLocation(container, path), ct)` |
| `RenameAsync(container, name, newContainer, newName, ct)` | `MoveAsync(source, destination, ct)` — both `BlobLocation`; non-atomic |
| `CopyAsync(container, name, newContainer, newName, ct)` | `CopyAsync(source, destination, ct)` — both `BlobLocation` |
| `GetPagedListAsync(container, pattern, pageSize, ct)` | `ListAsync(new BlobQuery(container, prefix, pageSize))` → `BlobPage`, or `GetBlobsAsync(query)` to stream |
| `GetBlobsAsync(container, pattern, ct)` (interface member) | `GetBlobsAsync(new BlobQuery(container, prefix))` (extension); glob via `GetBlobsAsync(query, globPattern)` |
| `DeleteAllAsync(container, blobSearchPattern, ct)` | `DeleteAllAsync(new BlobQuery(container, prefix), ct)` — prefix-based; glob-delete is list + filter + bulk-delete |
| `CreateContainerAsync(container, ct)` | `IBlobContainerManager.EnsureContainerAsync(container, ct)` — resolved from DI |
| `new BlobUploadRequest(stream, fileName, metadata)` / named `FileName:` | `new BlobUploadRequest(path, stream, metadata)` / named `Path:` |
| `BulkUploadAsync(...)` → `IReadOnlyList<Result<Exception>>` | `BulkUploadAsync(container, requests, ct)` → `IReadOnlyList<BlobBulkResult>` |
| `BulkDeleteAsync(...)` → `IReadOnlyList<Result<bool, Exception>>` | `BulkDeleteAsync(container, paths, ct)` → `IReadOnlyList<BlobBulkResult>` |
| metadata `Dictionary<string, string?>` | `IReadOnlyDictionary<string, string>?` (non-null values) everywhere |
| `GetPresigned{Download,Upload}UrlAsync(container, blobName, expiry, ct)` | `GetPresigned{Download,Upload}UrlAsync(new BlobLocation(container, path), expiry, ct)` |

Behavior changes to plan for beyond the signature swap:

- **Upload no longer auto-creates the container.** Uploading to a missing bucket/container now throws; call `IBlobContainerManager.EnsureContainerAsync` (or provision via IaC) first.
- **Container management is resolved, not cast.** Replace any `storage is I…ContainerManager` probe with `GetKeyedService<IBlobContainerManager>(name)`; expect `null` for CloudflareR2.
- **Pagination tokens are serializable.** A continuation token can now be carried across requests; the old closure cursor could not.
- **Metadata round-trips on FileSystem and SFTP** (via sidecar files) where it was previously discarded.

## Choosing a Provider

Pick one provider per store (default or named) based on where the bytes must live and which capabilities you need.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Blobs.FileSystem` | Local dev, testing, or single-node on-prem with no cloud dependency | Multi-node or horizontally-scaled deployments (no shared storage) | Not distributed; metadata kept in sidecar companion files; paging is emulated re-scan |
| `Headless.Blobs.Aws` | Production on AWS; need presigned URLs and bulk operations | Not on AWS, or egress cost is a concern | Ties you to S3 pricing and the AWS SDK |
| `Headless.Blobs.CloudflareR2` | S3-compatible storage with low egress cost and private buckets | You need public serving via ACLs, or in-app bucket provisioning | No ACL concept; no container-manager capability — buckets are provisioned out-of-band (IaC/dashboard) |
| `Headless.Blobs.Azure` | Production on Azure; want Entra ID auth and SAS presigned URLs | Not on Azure | Requires a `BlobServiceClient`; extra SAS rules for AAD clients |
| `Headless.Blobs.SshNet` | Files must land on a remote SFTP/SSH server or legacy system | High-throughput or presigned-URL workloads | Slower; no presigned URLs; sidecar metadata costs a second round-trip; opens live SSH connections |
| `Headless.Blobs.Redis` | Small, ephemeral blobs (thumbnails, temp uploads) needing fast access | Large files (default 10 MB cap) or durable storage | In-memory cost; `HSCAN` paging is unordered; not for large or long-lived blobs |

---

## Headless.Blobs.Abstractions

Defines the unified interfaces and value types for blob/file storage operations across all providers.

### Problem Solved

Application code needs a single, provider-agnostic API for file storage so it can switch between cloud providers, local storage, or test fakes without change. This package defines `IBlobStorage`, the `BlobLocation` address type, and the supporting contracts; it carries no implementation and no DI registrations.

### Key Features

- `IBlobStorage` — data-plane interface covering upload, download (`OpenReadStreamAsync`), copy, move (non-atomic), delete, exists, info, token-based listing (`ListAsync`), and bulk upload/delete.
- `BlobLocation` — validated `(Container, Path)` address value type; constructor enforces path security and offers a `params ReadOnlySpan<string>` segment overload.
- `BlobQuery` / `BlobPage` — token-based paging primitive: a prefix-scoped page request and its result plus an opaque continuation token.
- `BlobBulkResult` — identity-carrying bulk outcome (`Container` + `Path` + optional validated `BlobLocation` + `Result<bool, Exception>`).
- `IBlobContainerManager` — optional container-lifecycle capability (Ensure/Exists/Delete), resolved from DI; implemented by AWS, Azure, FileSystem, Redis, and SSH (not R2).
- `IPresignedUrlBlobStorage` — optional presigned GET + PUT URL capability over a `BlobLocation`; implemented only by AWS, Azure, and CloudflareR2.
- `IBlobStorageProvider` — resolves named `IBlobStorage` instances registered through the setup builder (`GetStorage(name)`, `GetStorageOrNull(name)`, `RegisteredNames`).
- `IBlobNamingNormalizer` — provider-specific two-tier path normalization contract applied by the provider's resolve step.
- `BlobStorageExtensions` — `GetBlobsAsync` streaming + glob filter, `GetBlobsListAsync` materializer, and `UploadContentAsync`/`GetBlobContentAsync` (text + JSON) convenience helpers.
- Consistent metadata typing: `IReadOnlyDictionary<string, string>?` with non-null values.

### Installation

```bash
dotnet add package Headless.Blobs.Abstractions
```

### Quick Start

```csharp
public sealed class FileService(IBlobStorage storage)
{
    public async Task UploadAsync(Stream file, string fileName, CancellationToken ct)
    {
        var location = new BlobLocation("uploads", "images", fileName); // container "uploads", key "images/<fileName>"

        await storage.UploadAsync(
            location,
            file,
            metadata: new Dictionary<string, string> { ["uploaded-by"] = "user-123" },
            cancellationToken: ct
        );
    }

    public async Task<string?> GetContentAsync(string fileName, CancellationToken ct)
    {
        var location = new BlobLocation("uploads", "images", fileName);

        // Dispose result promptly — holding it may exhaust connection pools.
        await using var result = await storage.OpenReadStreamAsync(location, ct);
        if (result is null)
            return null;

        using var reader = new StreamReader(result.Stream);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task<IReadOnlyList<BlobInfo>> ListImagesAsync(CancellationToken ct)
    {
        // Stream every page transparently; Prefix is pushed to the backend.
        var query = new BlobQuery("uploads", prefix: "images/");
        var blobs = new List<BlobInfo>();
        await foreach (var blob in storage.GetBlobsAsync(query, ct))
            blobs.Add(blob);
        return blobs;
    }
}
```

Resolve a named store, manage a container, or check for presigned support:

```csharp
public sealed class StorageService(
    IBlobStorageProvider provider,
    [FromKeyedServices("archive")] IBlobStorage archiveStorage
)
{
    // Validate an externally-supplied name before resolving:
    public bool IsKnownStore(string name) => provider.RegisteredNames.Contains(name);

    // Resolve by name:
    public IBlobStorage GetByName(string name) => provider.GetStorage(name); // throws if not found

    public IBlobStorage? TryGetByName(string name) => provider.GetStorageOrNull(name); // null if not found

    // Container management is resolved from DI, not cast from the store (null for R2):
    public async Task EnsureAsync(IServiceProvider sp, string container, CancellationToken ct)
    {
        var manager = sp.GetService<IBlobContainerManager>();
        if (manager is not null)
            await manager.EnsureContainerAsync(container, ct);
    }

    // Feature-detect presigned on the default store (is-cast is honest here):
    public async Task<Uri?> TryGetPresignedUrlAsync(IBlobStorage storage, BlobLocation location)
    {
        if (storage is not IPresignedUrlBlobStorage presigned)
            return null;
        return await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(15));
    }
}
```

### Configuration

None. This is an abstractions-only package.

### Dependencies

- `Headless.Extensions`
- `Headless.Serializer.Json`

### Side Effects

None. This is an abstractions package.

---

## Headless.Blobs.Core

Unified setup builder for composing one or more named blob stores in a single DI container.

### Problem Solved

A single application often needs several blob stores at once — images on one backend, documents on another, scratch files on a third — and sometimes two instances of the same provider (a production and a staging bucket). Registering providers directly only yields one `IBlobStorage`; a second registration silently shadows the first. This package adds `AddHeadlessBlobs(...)`, a single entry point that composes an optional default plus any number of independently-configured named stores, each resolvable by name.

### Key Features

- `AddHeadlessBlobs(Action<HeadlessBlobsSetupBuilder>)` — single registration entry point for all blob stores.
- Optional default store (at most one), injectable as plain `IBlobStorage`.
- Unlimited named stores with unique names; the same provider may back several.
- `IBlobStorageProvider` — resolves named stores by name; exposes `RegisteredNames` for safe pre-validation.
- Keyed `IBlobStorage` resolution via `[FromKeyedServices("name")]` or `GetRequiredKeyedService<IBlobStorage>("name")`.
- Deferred, gate-validated registration: a misconfigured setup (duplicate default, duplicate name, zero providers for a named store) throws before mutating the service collection.

### Design Notes

- Each provider package contributes `Use{Provider}` extension members on `HeadlessBlobsSetupBuilder` (default) and `HeadlessBlobInstanceBuilder` (named). Named stores register as keyed `IBlobStorage` services, never touching the default (unkeyed) registration, so a named-only configuration leaves plain `IBlobStorage` unregistered.
- Each store is fully isolated: its own named options, its own provider client, and its own `IBlobNamingNormalizer`. Ambient services (`IMimeTypeProvider`, `IClock`) are shared across stores.
- Two capabilities are surfaced differently, on purpose. Presigned support is a per-store cast: for named stores, AWS, Azure, and CloudflareR2 also register a keyed `IPresignedUrlBlobStorage` forward; for the default store, feature-detect by casting (`storage is IPresignedUrlBlobStorage`). Container management is a **separate** registration resolved from DI: AWS, Azure, FileSystem, Redis, and SSH register a default + keyed `IBlobContainerManager`, while CloudflareR2 registers none (so `GetKeyedService<IBlobContainerManager>` returns null for an R2 store) — this is why it cannot be an `is`-cast from the shared AWS storage type.
- `IBlobStorageProvider.RegisteredNames` contains only **named** instance names; the default/unnamed store is excluded. Use it to validate an externally-supplied name before calling `GetStorage` rather than probe-and-catch.

### Installation

```bash
dotnet add package Headless.Blobs.Core
```

Add at least one provider package (`Headless.Blobs.Aws`, `Headless.Blobs.Azure`, `Headless.Blobs.CloudflareR2`, `Headless.Blobs.FileSystem`, `Headless.Blobs.Redis`, or `Headless.Blobs.SshNet`).

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store — injected as plain IBlobStorage.
    blobs.UseCloudflareR2(options =>
    {
        options.AccountId = builder.Configuration["R2:AccountId"]!;
        options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
        options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
    });

    // Named store — injected as keyed IBlobStorage("docs").
    blobs.AddNamed(
        "docs",
        instance =>
            instance.UseAzure(
                setupAction: options => { },
                clientFactory: _ => new BlobServiceClient(builder.Configuration["Azure:Docs:ConnectionString"])
            )
    );

    // Two named instances of the same provider, each with independent config.
    blobs.AddNamed("scratch", instance => instance.UseFileSystem(options => options.BaseDirectoryPath = "/tmp/blobs"));

    blobs.AddNamed(
        "archive",
        instance => instance.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions("AWS:Archive"))
    );
});
```

Resolve stores:

```csharp
// Default store — plain injection.
public sealed class UploadService(IBlobStorage storage) { }

// Named store — keyed injection.
public sealed class DocsService([FromKeyedServices("docs")] IBlobStorage docsStorage) { }

// Named store — via IBlobStorageProvider.
public sealed class MultiStoreService(IBlobStorageProvider provider)
{
    public IBlobStorage GetDocs() => provider.GetStorage("docs");

    public bool HasStore(string name) => provider.RegisteredNames.Contains(name);
}

// Named container management (AWS/Azure/FileSystem/Redis/SSH — resolved, null for R2).
public sealed class ProvisioningService([FromKeyedServices("docs")] IBlobContainerManager? docsManager)
{
    public ValueTask EnsureAsync(string container, CancellationToken ct) =>
        docsManager?.EnsureContainerAsync(container, ct) ?? ValueTask.CompletedTask;
}

// Named presigned URL (AWS/Azure/R2 only).
public sealed class PresignedService([FromKeyedServices("docs")] IPresignedUrlBlobStorage presigned)
{
    public Task<Uri> GetDownloadUrl(BlobLocation location) =>
        presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromHours(1)).AsTask();
}
```

### Configuration

No options of its own. Each store's options are configured through its provider's `Use{Provider}` overloads (`Action<TOptions>`, `IConfiguration`, or `Action<TOptions, IServiceProvider>`).

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Extensions`

### Side Effects

- Registers `IBlobStorageProvider` as singleton (backed by the container's keyed `IBlobStorage` registrations).
- Registers a called-once marker that rejects a second `AddHeadlessBlobs` call on the same service collection.
- Default `Use{Provider}`: registers `IBlobStorage` as unkeyed singleton.
- `AddNamed(... Use{Provider})`: registers `IBlobStorage` as keyed singleton (`name`). For AWS, Azure, and CloudflareR2 also registers `IPresignedUrlBlobStorage` as keyed singleton (`name`). For AWS, Azure, FileSystem, Redis, and SshNet also registers `IBlobContainerManager` (default + keyed `name`); CloudflareR2 registers none. For SshNet, also registers a keyed `SftpClientPool` singleton (`name`).
- There is no global (unkeyed) `IPresignedUrlBlobStorage` registration.

---

## Headless.Blobs.Aws

AWS S3 implementation of `IBlobStorage` for storing files in Amazon S3.

### Problem Solved

Provides integration with AWS S3 for blob storage using the unified `IBlobStorage` abstraction, with per-store S3 client construction, presigned URL support, and an opt-in bucket-lifecycle capability.

### Key Features

- Full `IBlobStorage` implementation for AWS S3, routed through the shared resolve seam.
- Bulk upload/delete with optimized batching, returning identity-carrying `BlobBulkResult` lists.
- Native-token paging: `ListAsync` uses the S3 `ListObjectsV2` continuation token as the opaque `BlobPage` token.
- Two-tier name normalization: bucket name normalized to S3 rules; object-key path segments validated and preserved.
- Metadata support; `GetBlobInfoAsync` reads metadata from the HEAD response. (The list API omits per-object metadata, and its `Created` falls back to `LastModified`.)
- Presigned download/upload URLs over a `BlobLocation` via `IPresignedUrlBlobStorage` (named stores only; feature-detect via cast for the default store).
- Bucket lifecycle via a dedicated `AwsBlobContainerManager` resolved from DI (`EnsureContainerAsync` keeps a per-instance ensured-bucket cache). `UploadAsync` no longer auto-creates a missing bucket — that is an error.
- Per-store `IAmazonS3` constructed via `S3ClientFactory`; optional `AWSOptions` to override the SDK credential/region chain.

### Installation

```bash
dotnet add package Headless.Blobs.Aws
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Default store — AWS SDK credential/region chain applies unless overridden.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions())
);

// Explicit credentials:
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseAws(
        options => { },
        new AWSOptions
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("access-key", "secret-key"),
        }
    )
);

// Named store with per-store credentials; keyed IPresignedUrlBlobStorage registered automatically.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.AddNamed(
        "archive",
        instance => instance.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions("AWS:Archive"))
    )
);
```

Blobs are addressed by `BlobLocation`; the bucket must already exist:

```csharp
var location = new BlobLocation("my-bucket", "reports/q1.pdf");

// Provision the bucket first (resolved from DI; not a cast from the store):
var manager = serviceProvider.GetService<IBlobContainerManager>();
if (manager is not null)
    await manager.EnsureContainerAsync("my-bucket");

await storage.UploadAsync(location, stream);

// Presigned URL on the default store — feature-detect:
if (storage is IPresignedUrlBlobStorage presigned)
{
    var url = await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromHours(1));
}
```

### Configuration

#### appsettings.json

```json
{
  "AWS": {
    "Region": "us-east-1",
    "AccessKey": "your-access-key",
    "SecretKey": "your-secret-key"
  }
}
```

#### Options

```csharp
options.CannedAcl = S3CannedACL.Private;
options.UseChunkEncoding = true;
options.DisablePayloadSigning = false;
options.MaxBulkParallelism = 10;
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`
- `AWSSDK.Extensions.NETCore.Setup`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseAws(...))` or `AddNamed("name", i => i.UseAws(...))`:

- Default (`UseAws`): registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (`AwsBlobContainerManager`). The per-store `IAmazonS3` is constructed inline; it is not registered in the DI container.
- Named (`AddNamed ... UseAws`): registers `IBlobStorage`, `IPresignedUrlBlobStorage` (forwarded from the keyed `IBlobStorage`), and `IBlobContainerManager` each as keyed singleton (`name`). The per-store `IAmazonS3` is constructed inline.

---

## Headless.Blobs.Azure

Azure Blob Storage implementation of `IBlobStorage` for storing files in Azure.

### Problem Solved

Provides integration with Azure Blob Storage using the unified `IBlobStorage` abstraction, with `BlobServiceClient` resolution from DI or a per-store factory, presigned SAS URL support, and an opt-in container-lifecycle capability.

### Key Features

- Full `IBlobStorage` implementation for Azure Blob Storage, routed through the shared resolve seam.
- Bulk operations with the Azure Batch API, returning identity-carrying `BlobBulkResult` lists.
- Native-token paging: `ListAsync` uses the Azure `Pageable` continuation token as the opaque `BlobPage` token.
- Metadata support; `GetBlobInfoAsync` reads metadata from `GetPropertiesAsync` consistent with list metadata.
- Presigned download/upload URLs over a `BlobLocation` via `IPresignedUrlBlobStorage` (SAS-based; named stores only — feature-detect via cast for the default store).
- Container lifecycle via a dedicated `AzureBlobContainerManager` resolved from DI (ensured-container cache retained). `UploadAsync` no longer auto-creates a missing container — that is an error.
- Non-seekable upload streams pass through (no buffering).
- Per-store `BlobServiceClient` from an optional `clientFactory`; falls back to the ambient `BlobServiceClient` from DI.

### Installation

```bash
dotnet add package Headless.Blobs.Azure
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register a BlobServiceClient in DI (used when no clientFactory is supplied).
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]));

builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store — consumes the ambient BlobServiceClient from DI.
    blobs.UseAzure(options => { });

    // Named store on a different account — per-store clientFactory overrides the DI client.
    // Also registers keyed IPresignedUrlBlobStorage("archive") and IBlobContainerManager("archive").
    blobs.AddNamed(
        "archive",
        instance =>
            instance.UseAzure(
                setupAction: options => { },
                clientFactory: _ => new BlobServiceClient("<archive-connection-string>")
            )
    );
});
```

When no `clientFactory` is supplied, the `BlobServiceClient` must be registered in DI before first use. Absence is detected at resolution time, not at startup.

### Configuration

#### appsettings.json

```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
    }
  }
}
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Core`
- `Headless.Hosting`
- `Azure.Storage.Blobs`
- `Azure.Storage.Blobs.Batch`
- `Microsoft.Extensions.Azure`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseAzure(...))` or `AddNamed("name", i => i.UseAzure(...))`:

- Default (`UseAzure`): registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (`AzureBlobContainerManager`). Consumes `BlobServiceClient` from DI (or `clientFactory`) at resolution time.
- Named (`AddNamed ... UseAzure`): registers `IBlobStorage`, `IPresignedUrlBlobStorage` (forwarded from the keyed `IBlobStorage`), and `IBlobContainerManager` each as keyed singleton (`name`).
- Presigned URLs require a `BlobServiceClient` that can sign: account-key clients sign locally; AAD/`DefaultAzureCredential` clients fall back to user-delegation SAS (requires `Storage Blob Delegator` role, capped at 7 days). A bare SAS-token or anonymous client throws `InvalidOperationException` at call time.

---

## Headless.Blobs.CloudflareR2

Cloudflare R2 implementation of `IBlobStorage`, running R2 as a private, S3-compatible blob backend on the reused AWS S3 engine.

### Problem Solved

R2 speaks the S3 API but cannot use the AWS provider as-is: the endpoint, path-style addressing, and AWS SDK v4 checksum defaults need R2-specific configuration, and R2 has no ACL concept. This package configures an R2-tuned `IAmazonS3` via `R2ClientFactory` and reuses `AwsBlobStorage`, making R2 a drop-in, cost-saving S3 replacement.

### Key Features

- Full `IBlobStorage` implementation for Cloudflare R2 (reuses the AWS S3 engine and its resolve seam, native-token paging, and bulk results).
- Presigned download/upload URLs over a `BlobLocation` via `IPresignedUrlBlobStorage` (named stores only — feature-detect via cast for the default store).
- R2-correct client config: path-style addressing, `auto` region, SDK v4 checksum settings R2 accepts.
- R2 bucket naming normalization (no dots).
- Jurisdiction-aware endpoints (default, EU, FedRAMP).
- R2-safe defaults applied per named instance (`AwsBlobStorageOptions`): `CannedAcl = null`, `UseChunkEncoding = false`, `DisablePayloadSigning = true`.

### Design Notes

- **No container-manager capability.** R2's object-scoped tokens cannot create or manage buckets, so the package deliberately registers **no** `IBlobContainerManager` — `GetService`/`GetKeyedService<IBlobContainerManager>` honestly returns `null` for an R2 store. This is exactly why container management is a separately-resolved DI service rather than an `is`-cast from the shared `AwsBlobStorage` type: the cast could not distinguish AWS (capable) from R2 (not). Provision buckets out of band (IaC/dashboard). `UploadAsync` to a missing bucket is an error.
- **No ACLs / public access.** `CannedAcl` is `null`. Use presigned URLs for time-limited private access; public serving (custom domains / `r2.dev`) is out of scope.
- **Single PUT is capped at ~5 GiB**, the same as S3.

### Installation

```bash
dotnet add package Headless.Blobs.CloudflareR2
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store.
    blobs.UseCloudflareR2(options =>
    {
        options.AccountId = builder.Configuration["R2:AccountId"]!;
        options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
        options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
        // options.Jurisdiction = R2Jurisdiction.EuropeanUnion; // optional
    });

    // Named store — keyed IPresignedUrlBlobStorage("media") registered automatically.
    blobs.AddNamed(
        "media",
        instance =>
            instance.UseCloudflareR2(options =>
            {
                options.AccountId = builder.Configuration["R2Media:AccountId"]!;
                options.AccessKeyId = builder.Configuration["R2Media:AccessKeyId"]!;
                options.SecretAccessKey = builder.Configuration["R2Media:SecretAccessKey"]!;
            })
    );
});
```

Blobs are addressed by `BlobLocation`; buckets are provisioned out of band:

```csharp
var location = new BlobLocation("my-bucket", "reports/q1.pdf");

await storage.UploadAsync(location, stream);

// Feature-detect presigned on the default store:
if (storage is IPresignedUrlBlobStorage presigned)
{
    var url = await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(15));
}
```

### Configuration

#### appsettings.json

```json
{
  "R2": {
    "AccountId": "your-account-id",
    "AccessKeyId": "your-access-key-id",
    "SecretAccessKey": "your-secret-access-key",
    "Jurisdiction": "Default"
  }
}
```

Bind with `blobs.UseCloudflareR2(builder.Configuration.GetSection("R2"))`.

### Dependencies

- `Headless.Blobs.Aws` (the reused S3 engine)
- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseCloudflareR2(...))` or `AddNamed("name", i => i.UseCloudflareR2(...))`:

- Default (`UseCloudflareR2`): registers `IBlobStorage` as unkeyed singleton. The per-store `IAmazonS3` (R2-tuned) is constructed inline; it is not registered in the DI container. No `IBlobContainerManager` is registered.
- Named (`AddNamed ... UseCloudflareR2`): configures named `AwsBlobStorageOptions` with R2 forced defaults; registers `IBlobStorage` as keyed singleton (`name`); registers `IPresignedUrlBlobStorage` as keyed singleton (`name`, forwarded from the keyed `IBlobStorage`). No `IBlobContainerManager` is registered. The per-store `IAmazonS3` is constructed inline.

---

## Headless.Blobs.FileSystem

Local file system implementation of `IBlobStorage` for development and on-premises scenarios.

### Problem Solved

Provides local file system storage using the unified `IBlobStorage` abstraction, for development, testing, and on-premises deployments without cloud dependencies.

### Key Features

- Full `IBlobStorage` implementation using the local file system, routed through the shared resolve seam.
- Container mapping to directories under a configured base path.
- Metadata stored in a sidecar companion file (reserved `.hlmeta` suffix) beside each blob.
- Emulated paging: `ListAsync` enumerates sorted by key and encodes a lexicographic start-after-key as the opaque token.
- Container lifecycle via `FileSystemBlobContainerManager` resolved from DI (`EnsureContainerAsync` creates the root directory).
- No external service dependencies; cross-platform path handling.

### Design Notes

- **Universal metadata via sidecars.** The file system has no native blob-metadata concept, so metadata is persisted in a companion `.hlmeta` file written content-first after the blob. A missing sidecar reads as empty metadata, so reads stay safe across a crash window, but the pair is **non-atomic**. Sidecars are filtered from every listing/exists/count/delete result and never surface as blobs; a blob key ending in `.hlmeta` is rejected at `BlobLocation` construction. Deleting or moving a blob deletes/moves its sidecar, so re-uploading the same key without metadata returns no stale metadata.
- **Emulated paging tier.** `ListAsync` re-scans the directory sorted by key and resumes after the start-after-key token. The token is serializable and survives a request boundary, but stability is weaker than S3/Azure under concurrent writes (a blob inserted before the resume point can be skipped or repeated) — the same cost profile as the previous implementation.
- **Path-dir creation, not container creation.** `UploadAsync` creates the intermediate directories needed to write the blob, but a missing top-level container is still an error unless created via `EnsureContainerAsync`. Non-seekable upload streams are written straight to disk (no buffering).

### Installation

```bash
dotnet add package Headless.Blobs.FileSystem
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseFileSystem(options =>
        options.BaseDirectoryPath = Path.Combine(builder.Environment.ContentRootPath, "storage")
    )
);
```

### Configuration

#### appsettings.json

```json
{
  "FileSystemBlob": {
    "BaseDirectoryPath": "/var/data/blobs"
  }
}
```

#### Options

```csharp
options.BaseDirectoryPath = "/path/to/storage"; // required; the root directory for all containers
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Core`
- `Headless.Hosting`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseFileSystem(...))` or `AddNamed("name", i => i.UseFileSystem(...))`:

- Default (`UseFileSystem`): registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (`FileSystemBlobContainerManager`).
- Named (`AddNamed ... UseFileSystem`): registers `IBlobStorage` and `IBlobContainerManager` each as keyed singleton (`name`).
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for FileSystem stores.

---

## Headless.Blobs.Redis

Redis implementation of `IBlobStorage` for storing small, ephemeral blobs in Redis.

### Problem Solved

Provides high-speed blob storage for small files using Redis, for temporary files, cache data, or session-related binary content. Not a general-purpose store — the 10 MB default limit and Redis memory model make it unsuitable for large files.

### Key Features

- Full `IBlobStorage` implementation using Redis, routed through the shared resolve seam.
- Automatic key expiration support.
- Metadata stored in a separate info hash alongside blobs (atomic via Lua); returned by `OpenReadStreamAsync` and `GetBlobInfoAsync`.
- `HSCAN`-cursor paging: `ListAsync` uses the native cursor as the opaque token.
- Container lifecycle via `RedisBlobContainerManager` resolved from DI (`EnsureContainerAsync` is a no-op; Redis has no container concept).

### Design Notes

- Designed for small, ephemeral blobs (cache data, session files, temporary uploads). The default `MaxBlobSizeBytes` is 10 MB to prevent memory exhaustion; uploads above the cap are rejected, and non-seekable streams are buffered to memory under the same cap. For large files, use Azure Blob Storage or S3.
- **`HSCAN`-cursor paging tier.** `ListAsync` encodes the native `HSCAN` cursor as the opaque continuation token. The order is non-lexicographic and the same blob may surface more than once across a rehash — callers iterating to completion must tolerate duplicates. An ordered (sort-based) token is a deferred follow-up if a consumer needs stable order.
- **No real container.** `EnsureContainerAsync` is a no-op, `ContainerExistsAsync` is true when any key exists under the container prefix, and `DeleteContainerAsync` clears the prefix's keys.

### Installation

```bash
dotnet add package Headless.Blobs.Redis
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// The IConnectionMultiplexer must be set in code — it cannot be bound from appsettings.json.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseRedis(options => options.ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379"))
);
```

### Configuration

`RedisBlobStorageOptions` requires an `IConnectionMultiplexer` instance. The `UseRedis(IConfiguration)` overload cannot bind this interface property — options validation fails at startup if `ConnectionMultiplexer` is not set via the `Action<RedisBlobStorageOptions>` overload.

| Option | Default | Description |
|--------|---------|-------------|
| `ConnectionMultiplexer` | *(required)* | `IConnectionMultiplexer` instance for Redis. |
| `MaxBlobSizeBytes` | 10 MB | Maximum blob size in bytes. Set to `0` to disable the limit. |
| `MaxBulkParallelism` | 10 | Maximum parallelism for bulk operations. |

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Core`
- `Headless.Hosting`
- `Polly.Core`
- `StackExchange.Redis`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseRedis(...))` or `AddNamed("name", i => i.UseRedis(...))`:

- Default (`UseRedis`): registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (`RedisBlobContainerManager`); registers `TimeProvider`, `IJsonOptionsProvider`, and `IJsonSerializer` as singletons (each via `TryAdd`, so existing registrations are kept).
- Named (`AddNamed ... UseRedis`): registers `IBlobStorage` and `IBlobContainerManager` each as keyed singleton (`name`); same `TryAdd` registrations for shared services.
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for Redis stores.

---

## Headless.Blobs.SshNet

SFTP/SSH implementation of `IBlobStorage` for storing files on remote servers via SFTP.

### Problem Solved

Provides blob storage via SFTP/SSH for scenarios requiring file transfer to remote servers, legacy system integration, or secure file exchange with systems that do not expose a cloud API.

### Key Features

- Full `IBlobStorage` implementation using SFTP, routed through the shared resolve seam.
- SSH key and password authentication.
- Metadata stored in a sidecar companion file (reserved `.hlmeta` suffix) beside each blob.
- Emulated paging: `ListAsync` lists recursively, sorts by key, and encodes a start-after-key as the opaque token.
- Container lifecycle via `SshBlobContainerManager` resolved from DI (`EnsureContainerAsync` is a validated `mkdir -p`).
- Connection pooling (`SftpClientPool`); each store owns its own pool.

### Design Notes

- **Universal metadata via sidecars.** SFTP has no native blob-metadata concept, so metadata is persisted in a companion `.hlmeta` file written content-first after the blob — a second round-trip per metadata-bearing write/read. A missing sidecar reads as empty metadata; the pair is **non-atomic**. Sidecars are filtered from every listing/exists/count/delete result; a blob key ending in `.hlmeta` is rejected at `BlobLocation` construction. Deleting or moving a blob deletes/moves its sidecar.
- **Non-atomic `Move`.** `MoveAsync` is copy-then-delete with best-effort destination rollback; the sidecar moves with the blob. There is no atomic server-side rename.
- **Emulated paging tier + validated directory creation.** `ListAsync` re-scans recursively, sorted by key, resuming after the start-after-key token (weaker stability under concurrent writes). `EnsureContainerAsync` and the upload/move retry paths validate and normalize every path segment through the resolve seam, so created directories match where uploads are written. Non-seekable upload streams pass through to the SFTP write stream unbuffered.

### Installation

```bash
dotnet add package Headless.Blobs.SshNet
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options => options.ConnectionString = "sftp://user:password@sftp.example.com:22/home/user/uploads")
);
```

### Configuration

#### appsettings.json

```json
{
  "SftpBlob": {
    "ConnectionString": "sftp://user:password@sftp.example.com:22/home/user/uploads"
  }
}
```

#### SSH Key Authentication

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options =>
    {
        options.ConnectionString = "sftp://user@sftp.example.com:22/home/user/uploads";
        options.PrivateKey = File.OpenRead("/path/to/key");
        options.PrivateKeyPassPhrase = "optional-passphrase"; // nullable
    })
);
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Hosting`
- `Headless.Serializer.Json`
- `SSH.NET`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseSsh(...))` or `AddNamed("name", i => i.UseSsh(...))`:

- Default (`UseSsh`): registers `SftpClientPool` as unkeyed singleton; registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (`SshBlobContainerManager`).
- Named (`AddNamed ... UseSsh`): registers `SftpClientPool`, `IBlobStorage`, and `IBlobContainerManager` each as keyed singleton (`name`). Each named store owns its own pool instance bound to its named options.
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for SshNet stores.
