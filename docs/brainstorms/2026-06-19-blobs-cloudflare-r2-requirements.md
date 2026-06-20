---
date: 2026-06-19
topic: blobs-cloudflare-r2
---

# Cloudflare R2 Blob Backend + Presigned URLs

## Summary

Add a dedicated `Headless.Blobs.CloudflareR2` package that runs R2 as a private, cost-saving S3 replacement on the reused AWS blob engine with R2-correct client config. Introduce an `IPresignedUrlBlobStorage` capability interface (presigned GET + PUT) implemented by the S3 engine (AWS + R2) and the Azure provider (via SAS). Refactor the shared `AwsBlobStorage` engine to end the per-operation bucket round trips that hurt both backends.

---

## Problem Frame

R2 is not usable through the current AWS provider today, despite being S3-compatible. `SetupAwsS3.AddAwsS3BlobStorage` only accepts `AWSOptions`, which can't set `ForcePathStyle` or the SDK v4 checksum knobs — both required for R2. On `AWSSDK.S3 4.0.23.3` the flexible-checksum default (`WHEN_SUPPORTED`) emits CRC32 trailers / `aws-chunked` framing that R2 rejects with signature/checksum errors. R2 also has no ACL concept, while the engine always sets `CannedACL`.

Separately, `AwsBlobStorage` does a `HeadBucket` (and sometimes `PutBucket`) before almost every object operation: uploads auto-create the bucket every call, and reads/exists/delete pre-check bucket existence. That is wasted latency on S3 and a hard failure on R2, where API tokens are commonly scoped to objects only and cannot create buckets.

Presigned URLs are advertised in `src/Headless.Blobs.Aws/README.md` but do not exist — `IBlobStorage` has no URL surface and no blob provider implements one. The migration use case (serving private files to clients for a bounded time) needs them.

---

## Key Decisions

- **Dedicated package, reused engine.** `Headless.Blobs.CloudflareR2` references `Headless.Blobs.Aws` and reuses `AwsBlobStorage` verbatim. R2's data plane is pure S3, so a standalone engine would only duplicate it. The package owns R2 options, setup, an R2-correct naming normalizer, conformance tests, and docs.
- **Presigned URLs as a capability interface.** `IPresignedUrlBlobStorage` is a separate interface, not members on `IBlobStorage`. FileSystem, Redis, and SshNet have no native signing concept; a capability interface keeps `IBlobStorage` honest and avoids runtime `NotSupportedException` holes.
- **Pin request shaping in client config.** The R2 `AmazonS3Config` sets `RequestChecksumCalculation` and `ResponseChecksumValidation` to `WHEN_REQUIRED` (plus `DisablePayloadSigning` / `UseChunkEncoding=false` via options). Pinning behavior in one place immunizes R2 against future AWS SDK default flips, the category of change that breaks S3-compatible stores.
- **Opt-in, cached auto-create.** Auto-create becomes an `AutoCreateContainer` flag (AWS default true, R2 default false), and when on, the bucket check runs at most once per bucket per instance. Reads/exists/delete drop the bucket pre-check entirely.
- **Bump-gated drift detection.** Durability rides on the pinned config plus a creds-gated conformance run required on `AWSSDK.*` dependency-update PRs. No scheduled run; the gap between bumps is accepted.
- **Public access stays deferred.** Custom domains and CDN serving are out — they do not help presigned URLs (SigV4 and SAS bind the signature to the storage host) and belong to the deferred public-access scope.

---

## Requirements

**Packaging and R2 client**

- R1. `Headless.Blobs.CloudflareR2` references `Headless.Blobs.Aws` and uses `AwsBlobStorage` as its storage engine without duplicating S3 logic.
- R2. The R2 `IAmazonS3` is configured with `ServiceURL` derived from the account id (and optional jurisdiction), `ForcePathStyle = true`, region `auto`, and `RequestChecksumCalculation` / `ResponseChecksumValidation` set to `WHEN_REQUIRED`.
- R3. R2 options bind account id, access key, secret key, and an optional jurisdiction (default / EU / FedRAMP). Registration is `AddR2BlobStorage` exposing the standard overload trio (`IConfiguration`, `Action<TOptions>`, `Action<TOptions, IServiceProvider>`).
- R4. R2 defaults are R2-safe: `CannedAcl = null`, `UseChunkEncoding = false`, `DisablePayloadSigning = true`, `AutoCreateContainer = false`.
- R5. R2 ships a naming normalizer enforcing R2 bucket rules — 3–63 chars, lowercase letters, digits, and hyphens, no dots.

**Shared engine refactor (`Headless.Blobs.Aws`)**

- R6. Read, exists, delete, and download paths no longer pre-check bucket existence; the object operation runs directly and `NoSuchBucket` / 404 maps to not-found or null, the same as a missing key.
- R7. Auto-create on upload and copy is gated by an `AutoCreateContainer` option (AWS default true, R2 default false); when off, a missing bucket surfaces as a clear error rather than being created.
- R8. When auto-create is on, the bucket existence/create call runs at most once per bucket per storage instance via an in-process cache. Explicit `CreateContainerAsync` always creates regardless of the flag and primes the cache.

**Presigned URLs**

- R9. `Headless.Blobs.Abstractions` defines `IPresignedUrlBlobStorage` with a presigned-download (GET) method and a presigned-upload (PUT) method, each taking the container, blob name, and an expiry.
- R10. `AwsBlobStorage` implements it via S3 SigV4 (covering both AWS and R2); the Azure provider implements it via SAS. FileSystem, Redis, and SshNet do not implement it.
- R11. Consumers obtain the capability by feature detection (`storage is IPresignedUrlBlobStorage`) or direct injection; a provider that lacks it is detectable without triggering a runtime `NotSupportedException`.

**Testing and drift durability**

- R12. A creds-gated R2 integration project reuses the cross-provider conformance suite against real R2 and skips cleanly when R2 credentials are absent.
- R13. The R2 conformance run is a required check on `AWSSDK.*` dependency-update PRs; there is no scheduled run.
- R14. Presigned GET and PUT round-trips are covered by tests for AWS and Azure (fetch/upload through the signed URL, with an expiry boundary check).

**Documentation**

- R15. Update `docs/llms/blobs.md` and the affected READMEs (`Headless.Blobs.Aws`, `Headless.Blobs.Azure`, and the new `Headless.Blobs.CloudflareR2`) for the new package, the presigned capability, the auto-create behavior change, and R2 setup; fix the stale `BucketName` claim in the AWS README.

---

## Acceptance Examples

- AE1. **Covers R7.** R2 storage (auto-create off), bucket missing. **When** a blob is uploaded, **then** the call fails with a clear bucket-not-found error rather than creating the bucket.
- AE2. **Covers R6.** Bucket missing. **When** `GetBlobInfoAsync` or `OpenReadStreamAsync` is called, **then** it returns null instead of throwing.
- AE3. **Covers R8.** AWS storage (auto-create on). **When** 100 blobs are uploaded to the same bucket, **then** the bucket existence/create call is issued at most once.
- AE4. **Covers R11.** FileSystem storage. **When** a consumer evaluates `storage is IPresignedUrlBlobStorage`, **then** the result is false and no exception is thrown.
- AE5. **Covers R9, R10.** A presigned download URL is generated with a short expiry. **When** an unauthenticated HTTP client fetches it before expiry, **then** it receives the object; after expiry, **then** the request is denied.

---

## Scope Boundaries

**Deferred for later**

- Public / CDN access, including R2 custom domains and `r2.dev`. Does not help presigned URLs; reopens the public-access scope set aside for this work.
- Multipart upload / large-file streaming. The single-PUT ~5 GiB cap and in-memory buffering of non-seekable streams remain.
- Presigned POST-form and multipart-part URLs. These ride with the deferred multipart work.
- Scheduled / nightly conformance runs. Detection is bump-PR-only by decision.
- Multi-account named clients and keyed-DI factories for R2 (present in the reference library, not requested here).

---

## Dependencies / Assumptions

- `AWSSDK.S3` is a shared, centrally-managed package (`Directory.Packages.props`) used by Blobs.Aws, SES, SNS, and SQS. A separate pinned version for R2 is not feasible; durability relies on the explicit config plus the bump-PR conformance gate.
- Checksum lever: config-level `WHEN_REQUIRED` is assumed sufficient on `AWSSDK.S3 4.0.23.3` (~90% confidence). Fallback if live R2 still rejects writes is per-request `DisableDefaultChecksumValidation = true` — the lever the reference library uses on its pinned `4.0.7.1`. Only a live R2 round-trip confirms which is needed.
- Azure SAS generation requires account-key or user-delegation-key auth; a provider wired with a bare SAS-token or anonymous connection cannot presign. Verify the Azure provider's current auth model during planning.
- R2 API tokens are commonly object-scoped and cannot create buckets. R2 auto-create defaults off (R4), and pre-creating buckets out of band (dashboard / API) is a documented requirement, not a bug to design around.
- The engine refactor (R6–R8) changes the existing AWS provider's observable behavior. Accepted under the greenfield "prefer clean breaking changes" rule; existing AWS tests are updated to match.

---

## Outstanding Questions

**Deferred to planning**

- Presigned method signature shape: expiry as `TimeSpan` vs absolute time, return `Uri` vs `string`, and whether presigned PUT constrains content-type/length.
- Whether `AutoCreateContainer` lives on the shared `AwsBlobStorageOptions` or only on the R2 options surface.
- R2 jurisdiction endpoint derivation (default / EU / FedRAMP host construction).
- Azure SAS auth-mode detection (account key vs user delegation key) and how the provider selects it.
- CI wiring for the creds-gated conformance secret and the `AWSSDK.*`-bump required-check trigger.

---

## Sources / Research

- `src/Headless.Blobs.Aws/AwsBlobStorage.cs` — per-operation bucket dance: auto-create on upload (`:69`) and copy (`:387`); `DoesS3BucketExistV2Async` pre-check in `_ExistsAsync` (`:468`) reached by download (`:506`) and delete (`:179`); non-seekable stream buffered to memory (`:82`).
- `src/Headless.Blobs.Aws/AwsBlobStorageOptions.cs` — existing `UseChunkEncoding`, `DisablePayloadSigning`, `CannedAcl` knobs.
- `src/Headless.Blobs.Abstractions/IBlobStorage.cs` — current contract has no URL/presign surface.
- `src/Headless.Blobs.Aws/README.md` — stale `options.BucketName` claim (no such option exists).
- `tests/Headless.Blobs.Tests.Harness/BlobStorageTestsBase.cs` — cross-provider conformance suite to reuse for R2.
- Reference `Alos-no/Cloudflare.NET`: `src/Cloudflare.NET.R2/R2ClientFactory.cs` (config = `ServiceURL` + `ForcePathStyle=true` + `AuthenticationRegion="auto"`); `src/Cloudflare.NET.R2/R2Client.cs` per-request `DisablePayloadSigning` + `DisableDefaultChecksumValidation` (`:133`, `:226`); pins `AWSSDK.S3 4.0.7.1`.
- Verified in `AWSSDK.S3 4.0.23.3` / `AWSSDK.Core 4.0.7.1`: `ClientConfig.RequestChecksumCalculation` / `ResponseChecksumValidation` (enum values `WHEN_REQUIRED` / `WHEN_SUPPORTED`), `AmazonS3Config.ForcePathStyle`, `PutObjectRequest` / `UploadPartRequest.DisableDefaultChecksumValidation` and `.DisablePayloadSigning`.
