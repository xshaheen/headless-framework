---
date: 2026-06-26
topic: blobs-interface-redesign
---

# IBlobStorage Operational-Contract Redesign

## Summary

Redesign the `IBlobStorage` operational contract around a `BlobLocation(Container, Path)` value type that centralizes path validation and normalization in one place, a serializable token-based listing primitive (with streaming as sugar), prefix-first filtering, and capability interfaces for container management and universal metadata (via sidecar files on the filesystem-like backends). The new shape makes the path-handling bug class found in the cross-provider review impossible by construction.

## Problem Frame

The current interface carries a `string[] container` plus a separate `string blobName` on nearly every method. The first array element is "magic" — it maps to the provider-level root (S3 bucket, Azure container, SFTP/FS root) under strict naming rules, while the remaining segments plus `blobName` form a lenient key. This two-tier model is never expressed as a type; it is re-documented per method and re-implemented per call site via a normalize-and-validate helper (`_BuildObjectKey`, `_BuildBlobPath`).

The cross-provider review found three real bugs, each at a spot that skipped that helper:

- AWS/R2 `BulkDeleteAsync` built keys inline with an un-normalized bucket, so when the bucket name needed normalizing it targeted a wrong/nonexistent bucket — and the not-found handler then reported success for every entry (silent no-op deletion).
- FileSystem `GetBlobInfoAsync` combined the raw `blobName` into the path with no traversal check, letting `../` escape the base directory and probe arbitrary file metadata.
- SSH `CreateContainerAsync` built directories from raw segments with no validation or normalization, allowing traversal and creating directories that did not match where uploads were written.

These are symptoms of the shape, not independent mistakes: the contract relies on per-method discipline that has already failed. Secondary smells compound it — a closure-backed paging cursor that cannot survive a web-request boundary, glob matching re-implemented inconsistently per provider, container creation sitting on the data plane (an anti-pattern for S3), and metadata silently discarded by SFTP and FileSystem.

## Key Decisions

- **Addressing via `BlobLocation(Container, Path)`.** A store is a provider client/account that can address many buckets; `Container` selects the bucket per call and `Path` is the full object key within it. The value type validates provider-agnostic concerns (path traversal, control characters, absolute paths) once at construction; provider-specific normalization stays in the provider (the normalizer differs per backend). This collapses `Move`/`Copy` from four path parameters to two and removes the per-method discipline that bred the bugs.

- **Token-based listing core, streaming as sugar.** A single primitive returns a page plus an opaque continuation token the caller round-trips. `GetBlobsAsync` (`IAsyncEnumerable`) is rebuilt as an extension over it. S3/Azure encode their native tokens; FileSystem/SSH encode a start-after-key and re-scan; Redis uses its `HSCAN` cursor (or a sort-based start-after). Pagination guarantees differ by backend and are documented as explicit tiers: server-side stable on S3/Azure, emulated re-scan (weaker stability, same cost as today's O(n²)) on FileSystem/SSH/Redis.

- **Container lifecycle is a capability, off the data plane.** Container/bucket create (and related management ops) move to a separate `IBlobContainerManager`, mirroring the existing `IPresignedUrlBlobStorage` capability split. `UploadAsync` no longer creates the top-level container — a missing S3 bucket or Azure container is an error; callers provision via `IBlobContainerManager` or IaC first. FileSystem/SSH still create intermediate path directories as an inherent part of writing a file.

- **Prefix is first-class; glob is a client-side extension.** List/delete take a server-pushed `prefix`. Glob (`*`, `?`) becomes an explicit client-side filter through one shared matcher, layered over streaming. This ends the per-provider regex divergence and is honest about which filtering the server does versus the client.

- **Universal metadata via sidecar files.** FileSystem and SFTP gain full metadata support by storing metadata in a companion file beside each blob — the same shape Redis already uses (a separate info entry), but non-atomic on FS/SFTP. This closes the capability gap so metadata round-trips on all six providers. Sidecars are filtered from all listing/exists/delete/count paths, and the sidecar naming scheme is collision-proofed against real blob keys.

- **`Move` replaces `Rename`, documented as non-atomic.** Object stores have no native rename; it is copy-then-delete. The operation is renamed `Move` and its documentation states the non-atomic, best-effort-rollback reality instead of implying atomicity.

- **Unified bulk-result shape carrying blob identity.** Bulk operations return one result per input that identifies which blob it refers to (not positional), with a consistent per-item shape across bulk upload and bulk delete.

- **Consistent metadata typing.** Metadata uses one read-only dictionary shape with non-null values across the upload parameter, `BlobInfo`, and the download result.

## Requirements

**Addressing & validation**

- R1. The operational interface identifies a blob by a single `BlobLocation` value carrying a `Container` and a `Path`; `Move`/`Copy` take a source and destination `BlobLocation`.
- R2. `BlobLocation` validates provider-agnostic path security (path traversal, control characters, absolute paths) at construction, throwing `ArgumentException` on violation; every operational method routes through it so no method can skip validation.
- R3. Provider-specific normalization (bucket/container naming rules, key normalization) is applied by the provider when resolving a `BlobLocation`, not by the value type, because normalization rules differ per backend.

**Listing & filtering**

- R4. List/delete operations accept a server-pushed `prefix`; the prefix is the only filter pushed to the backend.
- R5. A single paged-listing primitive returns a page of `BlobInfo` plus an opaque, serializable continuation token; passing the token to a fresh call returns the next page, and a null token means no more pages.
- R6. A streaming `GetBlobsAsync` (`IAsyncEnumerable<BlobInfo>`) is provided as an extension over the paged primitive.
- R7. Glob matching (`*`, `?`) is an explicit client-side filter implemented by one shared matcher, not re-implemented per provider.

**Container lifecycle**

- R8. Container/bucket lifecycle operations live on a separate `IBlobContainerManager` capability interface; providers implement it only where the operation is meaningful (S3 may decline runtime bucket creation).
- R9. `UploadAsync` does not create the top-level container/bucket; a missing managed container/bucket is an error. FileSystem/SSH still create intermediate path directories required to write the file.

**Metadata**

- R10. All six providers support blob metadata; FileSystem and SFTP store it in a sidecar companion file alongside each blob.
- R11. Sidecar companions are excluded from every listing, existence, count, and delete-all result so they never surface as blobs or match a prefix/glob.
- R12. The sidecar naming scheme is collision-proofed: a blob key that would collide with the reserved sidecar form is rejected.

**Mutation operations**

- R13. `UploadAsync` rewinds seekable streams to position 0; non-seekable stream handling (buffer vs stream-through) is provider-specific and documented per provider rather than promised uniformly.
- R14. The move operation is named `Move`, performs copy-then-delete, and its documentation states it is non-atomic with best-effort rollback of the destination on source-delete failure; on FileSystem/SSH it moves the sidecar with the blob.
- R15. Bulk operations return one result per input that identifies the blob it refers to, with a consistent per-item result shape across bulk upload and bulk delete; a per-item failure does not abort the batch.

**Capabilities & typing**

- R16. Capability surfaces (`IPresignedUrlBlobStorage`, `IBlobContainerManager`) are discoverable by the caller (interface check/cast) so unsupported operations are explicit, not silently ignored.
- R17. Metadata uses one read-only dictionary shape with non-null string values across the upload parameter, `BlobInfo`, and the download result.

## Key Flows

- F1. Upload
  - **Trigger:** Caller uploads content to a `BlobLocation`.
  - **Steps:** `BlobLocation` validates the path; the provider normalizes container + key; if the managed container/bucket is missing the call fails; otherwise the provider writes the content (and, on FileSystem/SSH, the sidecar), creating intermediate path directories as needed.
  - **Outcome:** Blob stored with metadata retrievable on every provider.
  - **Covered by:** R1, R2, R3, R9, R10, R13.

- F2. List a page
  - **Trigger:** Caller requests a page for a container + prefix, optionally with a continuation token.
  - **Steps:** The provider pushes the prefix down, returns up to the page size of `BlobInfo` plus an opaque token; sidecar companions are filtered out; the caller round-trips the token for the next page.
  - **Outcome:** Serializable pagination usable across web requests; streaming available via the enumerable extension.
  - **Covered by:** R4, R5, R6, R11.

- F3. Move within a store
  - **Trigger:** Caller moves a blob from a source to a destination `BlobLocation`.
  - **Steps:** Copy source to destination, then delete source; on source-delete failure, best-effort delete the destination copy; on FileSystem/SSH the sidecar moves with the blob.
  - **Outcome:** Blob relocated; non-atomic semantics documented.
  - **Covered by:** R1, R14.

## Acceptance Examples

- AE1. Traversal rejected at construction
  - **Given** a `BlobLocation` built with a `Path` containing `../`, **When** it is constructed, **Then** an `ArgumentException` is thrown before any provider call. **Covers R2.**
- AE2. Bulk delete round-trips through normalization
  - **Given** blobs uploaded under a container name that requires normalization, **When** the same names are bulk-deleted, **Then** they are actually deleted (the bucket/keys resolve identically to upload) rather than silently reported as deleted. **Covers R1, R2, R3.**
- AE3. GetBlobInfo cannot traverse
  - **Given** `GetBlobInfo` called with a path that escapes the base, **When** it executes on FileSystem, **Then** it throws rather than probing an out-of-base file. **Covers R2.**
- AE4. Upload requires an existing managed container
  - **Given** an Azure container that does not exist, **When** a blob is uploaded, **Then** the upload fails; **and** after `IBlobContainerManager.EnsureContainer`, the same upload succeeds. **Covers R8, R9.**
- AE5. Continuation token survives a boundary
  - **Given** a first page and its continuation token serialized and passed to a fresh `ListAsync` call, **When** the next page is requested, **Then** it returns the subsequent items. **Covers R5.**
- AE6. Metadata round-trips via sidecar without leaking
  - **Given** a blob uploaded with metadata to FileSystem or SFTP, **When** it is read back, **Then** the metadata is returned; **and When** the container is listed, **Then** the sidecar companion does not appear as a blob. **Covers R10, R11.**
- AE7. Sidecar collision rejected
  - **Given** a blob key that matches the reserved sidecar form, **When** it is uploaded, **Then** it is rejected. **Covers R12.**

## Scope Boundaries

- Cross-store and cross-container coordination is out of scope: replication/fan-out, routing-by-container/prefix, primary-with-fallback chains, and cross-store `Move`/`Copy`. `Move`/`Copy` operate within a single store.
- Registration and resolution (the unified `AddHeadlessBlobs` builder, named stores, `IBlobStorageProvider`, keyed DI) are owned by the 2026-06-20 named-storage brainstorm and are not re-opened here; this redesign is the operational contract only.
- `xattr`/extended-attribute metadata is rejected in favor of sidecar files (fragile cross-platform and unsupported over SFTP via SSH.NET).
- Capability-segregating pagination (true paging only on S3/Azure, streaming-only elsewhere) is rejected in favor of emulated paging with documented guarantee tiers.

## Dependencies / Assumptions

- Greenfield: breaking changes to the operational contract are acceptable per CLAUDE.md.
- CloudflareR2 reuses `AwsBlobStorage`, so AWS-side changes propagate to R2 automatically (verified: the R2 package has a normalizer/options/client-factory/setup but no own storage class).
- The named-storage registration design is orthogonal and assumed in place; this work changes only the operational methods, not how stores are registered or resolved.
- Provider normalizers are genuinely provider-specific (S3/Azure/R2 strict bucket rules, `CrossOsNamingNormalizer` for FS/Redis/SSH), so `BlobLocation` can validate but not normalize.
- Conformance scenarios extend `Headless.Blobs.Tests.Harness` rather than copying fixtures; the existing path-traversal suite must grow to cover `GetBlobInfo`, bulk delete, and list/paging, plus a normalization round-trip test.

## Outstanding Questions

**Deferred to planning** — the core contract (the `(Container, Path)` shape, `EnsureContainer`) is decided; these are additive refinements that planning can settle.

- OQ1. Final surface of `IBlobContainerManager` — whether it includes `ContainerExists` and `DeleteContainer` or only `EnsureContainer`.
- OQ2. `BlobLocation` shape detail: single `Path` string versus an additional `params string[]` segments convenience constructor.
- OQ3. Exact sidecar naming scheme and the collision rule that enforces R12.
- OQ4. Sidecar write-ordering and partial-state recovery on FileSystem/SSH (content-first vs sidecar-first, orphan cleanup) given no transaction.
- OQ5. Per-provider continuation-token encoding (S3/Azure native; FileSystem/SSH start-after-key; Redis `HSCAN` cursor vs sort-based start-after) and whether listing is key-ordered per provider.
- OQ6. `Created` timestamp source on FileSystem (file creation time vs sidecar upload-date) for cross-provider consistency.
- OQ7. Where the shared glob matcher and the `BlobLocation` resolve/normalize helper live, and which providers can derive a literal prefix from a glob to push down.
- OQ8. Whether the 12 consistency findings (H1–L5) are folded into this redesign wholesale (current intent) or any are worth shipping as a pre-redesign pass.
- OQ9. Migration/changelog notes for the operational-contract break.

## Sources / Research

- `src/Headless.Blobs.Abstractions/IBlobStorage.cs` — current operational contract and its `string[] container` + `blobName` shape.
- `src/Headless.Blobs.Abstractions/Contracts/BlobInfo.cs`, `Contracts/BlobDownloadResult.cs` — metadata typing drift and nullability.
- `src/Headless.Blobs.Abstractions/Internals/PathValidation.cs`, `Internals/BlobStorageHelpers.cs` — shared validation and the `GetRequestCriteria` glob/prefix helper some providers bypass.
- `src/Headless.Blobs.Aws/AwsBlobStorage.cs` — `BulkDeleteAsync` normalization gap; `_BuildObjectKey` the canonical resolve path; list-vs-info `Created` divergence.
- `src/Headless.Blobs.FileSystem/FileSystemBlobStorage.cs` — `GetBlobInfoAsync` traversal gap; closure-backed `GetPagedListAsync`; metadata discarded.
- `src/Headless.Blobs.SshNet/SshBlobStorage.cs` — `CreateContainerAsync`/`_CreateContainerWithClientAsync` raw-segment handling; metadata discarded; non-atomic rename pre-delete.
- `src/Headless.Blobs.Redis/RedisBlobStorage.cs` — separate `blob-info/` hash (the sidecar pattern, atomic via Lua); `HSCAN`-based listing.
- `src/Headless.Blobs.Azure/AzureBlobStorage.cs` — native continuation-token pagination; metadata in list but not `GetBlobInfo`; non-seekable stream pass-through.
- `tests/Headless.Blobs.Tests.Harness/BlobStorageTestsBase.cs` — conformance suite; path-traversal coverage gaps for `GetBlobInfo`/bulk-delete/list.
- `docs/brainstorms/2026-06-20-blobs-named-storage-requirements.md` — registration/resolution design this work assumes and does not re-open.
