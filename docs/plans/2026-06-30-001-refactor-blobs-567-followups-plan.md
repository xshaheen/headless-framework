---
artifact_contract: x-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: x-brainstorm
date: 2026-06-30
module: Headless.Blobs
tags: [blobs, contract, multi-provider, move, bulk-delete, listing, dataprotection]
problem_type: contract-decision
tracker: https://github.com/xshaheen/headless-framework/issues/567
pr: https://github.com/xshaheen/headless-framework/pull/550
---

# Blobs #567 Follow-ups — Plan

## Goal Capsule

**Objective.** Drain the open items in tracking issue #567 on branch `xshaheen/blobs-interface-redesign` so PR #550 (IBlobStorage redesign) can land with a coherent, cross-provider operational contract and the data-loss class eliminated.

**Product authority.** Decisions below are settled (x-brainstorm session 2026-06-30). Greenfield framework — no deployed consumers; prefer the clean contract over compatibility. Providers: FileSystem, Aws (S3), Azure, Redis, SshNet (SFTP).

**Grounding.** Cross-ecosystem research (gocloud.dev/blob, Apache jclouds, MinIO, FluentStorage, native S3/Azure SDKs) confirms: (1) no mature abstraction offers Move/rename as a core primitive — it is caller-owned copy+delete; (2) overwrite-protection is universally a conditional precondition (`IfNotExist`/`If-None-Match`), never capture-and-restore; (3) list returns system fields only, user metadata is opt-in because it costs a HEAD-per-object.

**Open blockers.** None. CI green on PR #550.

---

## Product Contract

### Settled contract decisions

- **A1 — MoveAsync rejects an occupied destination.** `MoveAsync(source, destination)` returns `false` without copying when `destination` already exists (resolved-key equality). Eliminates the rollback-data-loss class and makes rollback safe by construction: the compensating delete can only ever remove the copy this Move just created. Self-move (resolved `source == destination`) remains a no-op returning `true`. Applies to all five providers. Consumers wanting overwrite semantics do Delete-then-Move.
- **A2 — DeleteAll throws on any per-key failure; BulkDelete returns per-key results.** `DeleteAllAsync` keeps throw-on-any (aggregate) — clearing a container is a single logical op and partial failure is genuinely exceptional. `BulkDeleteAsync` keeps its per-key `BlobBulkResult[]` (not-found ⇒ `Ok(false)`, real error ⇒ `Fail`). No contract change; the live defect is B1.
- **A3 — Uniform not-found⇒`Ok(false)`, parallelized.** Keep the cross-provider guarantee that deleting a missing key reports `Ok(false)`. AWS's sequential HEAD pre-check (the only mechanism that synthesizes the flag on idempotent S3) is parallelized to `MaxBulkParallelism`, matching `BulkUploadAsync`.
- **A4 — Listings omit metadata by default; `BlobQuery.IncludeMetadata` opts in.** New `BlobQuery.IncludeMetadata` (default `false`). When `false`, every provider returns `BlobInfo.Metadata == null` in listings (uniform). When `true`: Azure passes `BlobTraits.Metadata`, Redis reads its info-hash, FileSystem reads sidecars, S3/SSH issue a per-object HEAD/stat. Default path costs no extra round-trips on any provider.

### Required fixes (no contract choice)

- **B4 — Shared bulk orchestration.** `BlobStorageHelpers.RunBulkAsync<T>` owns the `Parallel.ForAsync(maxParallelism)` + indexed-result + `OperationCanceledException`-filter pattern duplicated across AWS/Azure/SSH/Redis. Foundation for B1 and A2; FileSystem may pass `maxParallelism: 1`.
- **B1 — Un-clearable container.** Azure/Redis `DeleteAllAsync` must delete the listed backend keys directly (as AWS does) instead of re-wrapping them in `new BlobLocation(...)`, which re-validates and can hard-fail on a backend-legal-but-BlobLocation-illegal key (`.hlmeta`, traversal, out-of-band writes) — permanently un-clearable today.
- **B2 — Move rollback harmonization.** Align the rollback exception filter across providers: bare catch + `CancellationToken.None` compensating delete. Document Redis as the atomic (no-rollback) tier. Trivial once A1 lands.
- **B3 — `BlobDownloadResult.FileName`.** Align FileSystem to `location.Path` (full container-relative key), matching AWS/Azure/Redis/SSH.
- **B5 — FS `ListAsync` bounded memory.** Port SFTP's O(pageSize+1) sliding-window selection, replacing the load-all-sort-take.
- **B6 — Move rollback test.** AWS mock: CopyObject succeeds, source DeleteObject throws → assert the compensating delete fires and no data loss. Pairs with A1/B2.

### Dropped from scope (stale — verified resolved in-tree; update #567)

- Redis `DeleteAll` "swallow" — already throws via `CountDeletedOrThrow` (`RedisBlobStorage.cs:411`).
- SSH mkdir race (#539) — already guarded (`SshBlobStorage.cs:760`: catches concurrent-create `SshException`, re-checks `Exists`).
- not-found⇒`Ok(false)` "unachievable" (#537) — already uniform across all five providers; only the AWS HEAD cost remained (→ A3).

### DataProtection (separate slice — also open in #567)

- **DataProtection keyed container ensure.** `PersistKeysToBlobStorage` DI overloads resolve the unkeyed `IBlobContainerManager` via `GetService`; a keyed/named store registers it keyed only, so ensure silently no-ops and the first key write fails on Azure/S3. Add a service-key parameter to the DI-resolving overloads; resolve `GetKeyedService<IBlobContainerManager>(key)` / `GetRequiredKeyedService<IBlobStorage>(key)` when supplied.

### Success criteria

- All five providers reject occupied-destination moves; no Move path can destroy pre-existing destination content.
- Azure/Redis containers are always clearable regardless of resident key shapes.
- Default listings allocate O(pageSize) and issue zero metadata round-trips; `IncludeMetadata` populates uniformly where requested.
- `make build` + blob conformance suite green; new AWS rollback test passes.
