---
domain: TUS (Resumable Uploads)
packages: Tus, Tus.Azure, Tus.S3, Tus.DistributedLocks
---

# TUS (Resumable Uploads)

> TUS protocol support for resumable file uploads: upload endpoints that bind each upload to its owner and tenant, validate it, and hand the finished file to the application, over Azure Blob Storage or S3-compatible (Cloudflare R2, Amazon S3, MinIO) stores, with distributed lock support.

## Orientation

Four packages compose the TUS domain:

| Package | Role |
|---|---|
| `Headless.Tus` | Root package: `AddHeadlessTus` (store selection, upload endpoints, expired-upload cleanup), `MapHeadlessTus`, the validator and completion-handler contracts, the tenancy seam, and `TusCorsDefaults`/`WithTusHeaders()` for browser clients. Carries the shared `tusdotnet` + `Headless.Hosting` references. |
| `Headless.Tus.Azure` | Store: `UseAzure` registers `TusAzureStore`, which keeps uploads in Azure Blob Storage as block blobs. |
| `Headless.Tus.S3` | Store: `UseS3` registers `TusS3Store`, which keeps uploads as S3 multipart uploads in Cloudflare R2, Amazon S3, or an S3-compatible server, through the app's `IAmazonS3` client. |
| `Headless.Tus.DistributedLocks` | Locking add-on: `UseDistributedLocks` makes requests for one upload exclude each other across nodes. |

The default path has three steps:

1. Call `services.AddHeadlessTus(tus => ...)` once and select exactly one store in it: `UseAzure`, `UseS3`, or `UseStore` for any other `ITusStore` ([Choosing a store](#choosing-a-store)).
2. Declare the upload endpoints in the same call with `tus.AddUploadEndpoint(...)`, and map each one with `app.MapHeadlessTus(pattern)`.
3. Add `tus.UseDistributedLocks()` when more than one node serves uploads.

tusdotnet serves the protocol. `MapHeadlessTus` adds what a store alone does not give an application:

- **Isolation.** Every upload is bound to the endpoint, owner, and tenant that created it, and is "not found" for anyone else ([Isolation](#isolation)).
- **Validation.** A creating request must pass the endpoint's size, type, and metadata rules, and the upload's first bytes must match the type it declared ([Validation](#validation)).
- **Completion.** The application's handler receives each finished upload, and receives it again on the client's next status request when it failed ([Completion handler](#completion-handler)).

An endpoint mapped with tusdotnet's own `MapTus` gets none of the three ([Raw MapTus](#raw-maptus)).

A complete resumable-upload backend on Azure (Azurite for local development: `docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0`):

```csharp
using System.Security.Claims;
using Headless.Tus;
using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

// 1. The storage account ("UseDevelopmentStorage=true" targets Azurite).
builder.Services.AddAzureClients(clients =>
    clients.AddBlobServiceClient(builder.Configuration.GetConnectionString("AzureStorage")));

// 2. The store, with options from the "Tus" config section (ContainerName, BlobPrefix, ...), and one
//    upload endpoint. Both are validated at startup. The job that removes expired incomplete uploads
//    runs by default.
builder.Services.AddHeadlessTus(tus =>
{
    tus.UseAzure(builder.Configuration.GetSection("Tus"));

    tus.AddUploadEndpoint(options =>
        {
            options.MaxSize = 2L * 1024 * 1024 * 1024; // required
            options.AllowedContentTypes.Add("video/mp4");
            options.RequiredMetadata.Add("filename");
        })
        // Who owns an upload. Without OwnedBy it is ICurrentUser.UserId.
        .OwnedBy(context => context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
});

// 3. CORS for browser clients on another origin (tus-js-client, Uppy): expose the tus
//    response headers or every cross-origin upload fails on the first request.
builder.Services.AddCors(options =>
    options.AddPolicy("tus", policy => policy.WithOrigins("https://app.example.com").WithTusHeaders()));

// Authentication is the app's own: add its scheme here, for example .AddJwtBearer().
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseCors("tus");
app.UseAuthentication();
app.UseAuthorization();

// 4. The tus endpoint. An upload's URL is /files/{id}.
app.MapHeadlessTus("/files").RequireAuthorization();

app.Run();
```

The S3 store swaps step 1 for an `IAmazonS3` registration and `UseAzure` for `UseS3` ([Headless.Tus.S3](#headlesstuss3)).

Any tus 1.0.0 client works against this endpoint (`tus-js-client`, Uppy's `@uppy/tus`, tuspy, …). A runnable example lives in [demo/Headless.Tus.Demo](../../demo/Headless.Tus.Demo/README.md): an Azure-backed host with a React frontend that drives a tus endpoint with both Uppy Dashboard and `use-tus`. The demo registers its store through `AddHeadlessTus` and maps its endpoint with tusdotnet's `MapTus`, so it shows the protocol and the store, not the upload endpoint layer.

## Agent Rules

- Register TUS once with `services.AddHeadlessTus(tus => ...)` and select exactly one store in it: `tus.UseAzure(...)`, `tus.UseS3(...)`, or `tus.UseStore(...)` for any other `ITusStore`, such as tusdotnet's `TusDiskStore`. `Headless.Tus` ships no store. No store, a second store, or a second `AddHeadlessTus` call throws `InvalidOperationException` as the call runs.
- Map uploads with `app.MapHeadlessTus(pattern)`, or `MapHeadlessTus(pattern, name)` for a named endpoint, not with tusdotnet's `MapTus`. Only `MapHeadlessTus` applies the endpoint's isolation, validation, completion handler, lock provider, and request body limit. A `MapTus` endpoint applies none of them, and an upload created through one is "not found" on a `MapHeadlessTus` endpoint ([Raw MapTus](#raw-maptus)).
- Every request to an upload endpoint needs an owner key: the result of the endpoint's `OwnedBy(...)` function, or `ICurrentUser.UserId` when the endpoint has none (`builder.AddHeadless()` registers `ICurrentUser`). A request with no key is refused with 401. The layer trusts the key as returned and never checks that the caller may act for it, so the authorization policy on the mapped endpoint must prove that, above all when the key is a workspace or account id read from the route ([Isolation](#isolation)).
- Set `MaxSize` on every upload endpoint. It is required, and startup fails when it is not positive. `MapHeadlessTus` also makes it the request body limit of the endpoint's requests; a reverse proxy or IIS in front of the host keeps its own limit, which code cannot lift ([Request body limits](#request-body-limits)).
- Write a completion handler (`OnCompleted<T>()`) that is safe to repeat and short. It runs at least once, runs again on the client's next status request after it threw (S3 and Azure stores), and can run on two nodes at once without `UseDistributedLocks`. Return `TusUploadHandoff.Reject` on the first run only: a reject reached on a re-run makes the client upload the whole file again before it sees the refusal. An upload the handler kept is the application's to delete and never expires ([Completion handler](#completion-handler)).
- A type allow-list (`AllowedContentTypes`, `AllowedFileExtensions`) also checks the upload's first bytes. Container formats are verified to their container only: an allowed docx, xlsx, or pptx accepts any ZIP archive, and an allowed doc, xls, or ppt accepts any compound file ([Validation](#validation)).
- In a tenant-aware host add `tenancy.Tus(t => t.ScopeByTenant())`. An upload carries its tenant in its stamp either way; the call makes a tenant mandatory and makes the `UseS3` and `UseAzure` stores keep each tenant's uploads under `.tenants/{tenant}/`. A store passed to `UseStore` is bound by the stamp alone ([Tenant scoping](#tenant-scoping)).
- Code that reads an upload through the store outside the endpoint, such as a job, sees the server's `headless-endpoint`, `headless-owner`, and `headless-tenant` entries in the upload's raw metadata. Clients may not send a metadata key that starts with `headless-` (400).
- Browser clients on another origin need the tus CORS surface: use `policy.WithTusHeaders()` (allowed request headers + exposed response headers + methods incl. PATCH/DELETE). Without the exposed headers, `tus-js-client`/Uppy cannot read `Location`/`Upload-Offset` and uploads fail on the first request.
- The job that removes expired incomplete uploads runs by default: first pass at startup, then every 5 minutes, and it scans the store each pass. Tune it or turn it off with `tus.ConfigureExpiredUploadsCleanup(options => ...)` ([Expired-upload cleanup](#expired-upload-cleanup)). It removes expired incomplete uploads only, and does nothing on a store that does not implement `ITusExpirationStore`. How long an incomplete upload lives is the endpoint's `TusUploadEndpointOptions.Expiration` (24 hours after its last write by default). The reaper runs outside tusdotnet's middleware and holds no `ITusFileLock`, so each store deletes an expired upload only if it is unchanged since the pass found it expired (`TusAzureStore` checks the blob ETag, `TusS3Store` the state object's ETag): a resume that committed data or refreshed the sliding expiration in between makes the delete skip that upload (not counted, not an error). A resume that has begun but not yet written anything can still be deleted mid-resume — gate uploads and cleanup with `Headless.Tus.DistributedLocks` to eliminate that window.
- `tus.UseAzure(...)` (overloads: `IConfiguration`, `Action<TusAzureStoreOptions>`, `Action<TusAzureStoreOptions, IServiceProvider>`) requires a `BlobServiceClient` registration and resolves `ITusAzureBlobHttpHeadersProvider`/`ITusFileIdProvider` when present (defaults otherwise); options run through FluentValidation with `ValidateOnStart`. A store constructed by hand (`new TusAzureStore(blobServiceClient, options)`) is selected with `tus.UseStore(store)`: it has no container provisioner and keeps no per-tenant layout.
- `TusAzureStore` rejects a write from a PATCH whose view of the blob is stale (it lost its file lock and another PATCH committed meanwhile) with `Azure.RequestFailedException` status `412`, deliberately not a `TusStoreException`. Through tusdotnet it surfaces as an unhandled exception, so the server answers `500` and sends no `Upload-Offset`; tus clients retry a 5xx by issuing HEAD and resuming from the committed offset. Do not map that exception to `400` (or any non-retryable 4xx) in an exception handler: tus clients treat `400` as fatal. Mapping it to `409` keeps it retryable.
- For multi-instance deployments, always add `Headless.Tus.DistributedLocks` and call `tus.UseDistributedLocks()` inside `AddHeadlessTus`. Without it, concurrent PATCH requests for the same file from different nodes interleave on one upload: on Azure they can corrupt block lists, and on S3 the write fencing fails all but one of them with a 5xx the client has to retry. A completion handler can also run on two nodes at once for one upload.
- `UseDistributedLocks` requires `IDistributedLock` in DI. Register a Headless distributed lock provider, for example: `services.AddHeadlessDistributedLocks(setup => setup.UseRedis())`.
- `MapHeadlessTus` uses the registered `ITusFileLockProvider` on its own. On a raw `MapTus` endpoint, set `FileLockProvider` in the `DefaultTusConfiguration` factory to the provider resolved from DI. A single-node deployment can omit the lock provider and use tusdotnet's in-process lock.
- `BlobMaxChunkSize` must be ≤ 100 MB (a store-imposed memory cap — each chunk is buffered per request; Azure's own per-block limit is 4,000 MiB), and `BlobDefaultChunkSize` must be 1 byte–100 MB and not exceed `BlobMaxChunkSize`. The store auto-selects chunk size from `BlobDefaultChunkSize` vs `BlobMaxChunkSize` based on declared upload length.
- With `EnableChunkSplitting = false` the whole PATCH body is buffered in memory and staged as one Azure block. `MaxNoSplitBufferSize` (default 256 MB, 1 byte–~2 GB) caps that buffer and is the store's *only* in-flight memory bound for a Creation-Defer-Length no-split upload — its declared length is unknown, so the too-much-data guard is a no-op, and the only other bound is the request body limit (an upload endpoint's `MaxSize`; the server's own limit on a raw `MapTus` endpoint). A body exceeding the cap is rejected with a `TusStoreException` (HTTP 400 through the pipeline). For a declared length, tusdotnet rejects an over-length PATCH itself with 413 before the store buffers anything. Ignored when splitting is enabled.
- An upload is capped at Azure's 50,000 committed blocks; a too-small chunk size on a very large upload throws a `TusStoreException` at commit time. Increase `BlobDefaultChunkSize`/`BlobMaxChunkSize` to use fewer, larger blocks.
- File ids become blob names: ids from a custom `ITusFileIdProvider` must not start with `.` (the reserved `.tenants/` folder starts with one) or contain `/`, `\`, `..`, `,` (the separator used to persist file-id lists in blob metadata), control characters, or leading/trailing whitespace — the store rejects such ids with a `TusStoreException` at every entry point in addition to enforcing the provider's own `ValidateId`.
- `GetExpirationAsync` returns `null` for completed uploads (deliberate `TusDiskStore` divergence): tusdotnet's `FileHasNotExpired` requirement 404s any upload whose expiration passed — reporting the stale timestamp would make completed uploads unreachable via tus (no HEAD, no DELETE/termination) after the sliding window elapses.
- Cancellation-token policy (TusDiskStore parity): client reads use the live request token — it is the disconnect signal — while everything that persists already-received bytes runs on `CancellationToken.None`. `AppendDataAsync` commits the bytes received before a disconnect (the spec: the server "SHOULD always attempt to store as much of the received data as possible"), and `SetExpirationAsync`/`VerifyChecksumAsync` ignore the token entirely: tusdotnet invokes them after the PATCH with the request token already cancelled — exactly when the bookkeeping (sliding-expiration refresh, checksum verify/rollback) must still complete. One exception: the `EnableChunkSplitting=false` fast path for seekable bodies streams directly into `StageBlock` with the live token (tusdotnet request bodies are never seekable; that path is store-direct only). Expired-file deletion likewise runs on `CancellationToken.None` once a file is enumerated, and a cancelled enumeration returns the files found so far (`TusDiskStore` parity). One deliberate divergence: a checksum PATCH whose chunk was fully received and hashed just before the disconnect is *committed* (its digest verifies), whereas `TusDiskStore` discards any chunk from a disconnected request — keeping verified bytes serves the spec better, and the resuming client sees the advanced offset on HEAD.
- The client's `Upload-Metadata` header value is stored verbatim in a single `tus_metadata` blob-metadata entry and echoed byte-for-byte in HEAD responses (TUS round-trip contract). Keys keep their original casing and characters; values may decode to any UTF-8 content. Upload-Metadata that is not printable ASCII or exceeds ~7 KB (Azure's 8 KB blob-metadata cap) is rejected with a `TusStoreException`. On an upload endpoint the stored string also holds the server's `headless-` entries, which count toward that limit and which a HEAD through the endpoint leaves out ([Isolation](#isolation)). A concatenation *final* also persists `tus_partial_uploads` (comma-joined constituent ids) alongside the verbatim metadata, so `CreateFinalFileAsync` re-checks the composed total against the 8 KB cap and throws a `TusStoreException` rather than an opaque Azure 400 when many partials plus near-cap metadata would overflow it.
- Checksum verification (SHA1, SHA256, SHA512, MD5) is handled automatically for both `Upload-Checksum` forms. Header flow: blocks are staged but not committed until the checksum passes; mismatched blocks are left uncommitted (Azure auto-removes them after 7 days). Trailer flow (`checksum-trailer`): data commits during the append with a recorded rollback offset (`tus_last_chunk_offset`); `VerifyChecksumAsync` hashes the committed range on demand and rolls the chunk back on mismatch or a faulty/missing trailer.
- The concatenation extension uses server-side block copy (`StageBlockFromUri`) when available, falling back to streaming when it is not — Azurite returns HTTP 501, and a private container returns HTTP 403 for the bare source URI (no SAS).
- `TusAzureStore` never creates its container; the constructor makes no network call. `tus.UseAzure(...)` registers the container as a [provisioned resource](utilities.md#resource-provisioning) of kind `AzureBlob`, named after `ContainerName`: the deployment runs `dotnet <app>.dll resources provision` (or `deploy`), which creates it with `ContainerPublicAccessType`, and a starting host only checks that it exists, holding the `warmup` readiness check until it does. A development or test host calls `services.AddDeployStepsOnStartup()` to create it as the host starts. A manually constructed store has no provisioner, so create its container beforehand. `resources purge` deletes the blobs under `BlobPrefix` and keeps the container; `resources delete` deletes them too, then the container only when nothing else is left in it, since a container can hold blobs outside `BlobPrefix`.
- `ITusAzureBlobHttpHeadersProvider` lets you inject custom `Content-Type` / cache headers per file. The default sets `application/octet-stream` for every upload ([Default content type](#default-content-type)).
- `tus.UseS3(...)` takes `IAmazonS3` from DI and does not configure it. For Cloudflare R2, register the client with path style, `AuthenticationRegion = "auto"`, and `WHEN_REQUIRED` request and response checksums (R2 rejects the SDK's default CRC checksums), and set `Provider = TusS3Provider.R2`. See [Headless.Tus.S3](#headlesstuss3) for one client example per server.
- `TusS3Store` does not support the Checksum extension, and an incomplete upload's content is not readable (`GetContentAsync` throws until the upload completes). On R2, an upload must finish within 7 days of its first part, when R2 aborts the multipart upload; keep the tus expiration below that.
- A deferred-length upload on `TusS3Store` can grow to `(10,000 − 4 × PartClaimWindow) × PartSize` bytes, 77,952 MiB (about 76 GiB) at the defaults; a known length up to `(10,000 − 4 × PartClaimWindow) × MaxPartSize`, 623,616 MiB (about 609 GiB). Raise `PartSize` for larger deferred uploads.
- `TusS3Store` never creates its bucket and never edits bucket lifecycle rules. Run `resources provision -k S3Bucket` as a deploy step; on R2 that step needs a token that can create buckets, while the serving host needs only object rights.

## Core Concepts

### TUS Protocol Flow

TUS is an HTTP-based protocol for resumable uploads. The key operations are:

1. **POST /files** — Upload creation. Client declares `Upload-Length` (or defers it). Server returns a `Location` URL with a file ID and `Upload-Offset: 0`.
2. **PATCH /files/{id}** — Chunk upload. Client sends bytes starting at `Upload-Offset`. Server appends and returns the new offset.
3. **HEAD /files/{id}** — Resume check. Client fetches current `Upload-Offset` to know where to resume after a disconnect.
4. **DELETE /files/{id}** (Termination extension) — Cancels and cleans up.

`tusdotnet` handles the HTTP negotiation; the store (`TusAzureStore` or `TusS3Store`) handles where bytes land; an upload endpoint decides who may reach an upload, what may be uploaded, and what happens when it finishes.

### Choosing a store

| Store | Use when | Trade-off |
|---|---|---|
| `Headless.Tus.Azure` | Uploads land in Azure Blob Storage, or clients send `Upload-Checksum`. | Supports every tus extension tusdotnet defines, Checksum included. |
| `Headless.Tus.S3` | Uploads land in Cloudflare R2, Amazon S3, or an S3-compatible server. | No Checksum extension. An incomplete upload's content is not readable, only its metadata. On R2 an upload must finish within 7 days of its first part. |

Both stores fence every write against stale writers, report completed uploads as never expiring, record a handoff so a failed completion handler runs again, keep each tenant's uploads under their own path segment when uploads are scoped by tenant, and register their storage as a [provisioned resource](utilities.md#resource-provisioning). Moving from one to the other means swapping the client registration and the `Use*` member; uploads in flight do not move.

Any other `ITusStore` goes through `tus.UseStore(...)`, tusdotnet's `TusDiskStore` included. Unless it implements `ITusHandoffStore`, such a store runs a completion handler once per upload. It has no per-tenant layout and no provisioner.

An upload endpoint offers the tus extensions its store supports except concatenation. What the store sections below say about concatenation applies to the store itself and to a raw `MapTus` endpoint.

### Azure Block Blob Mapping

Azure Block Blobs decompose a file into independently-uploadable blocks that are committed in a single atomic operation. `TusAzureStore` maps TUS chunks onto Azure blocks:

- Each PATCH appends one or more staged blocks (split by `BlobDefaultChunkSize` / `BlobMaxChunkSize`).
- A block list commit makes the blocks durable and visible as blob content.
- For checksum uploads, blocks are staged but the commit is deferred until `VerifyChecksumAsync` passes — if checksum fails, staged blocks are left uncommitted and Azure discards them within 7 days.

This is why `GetUploadOffsetAsync` sums committed block sizes, not the blob's `ContentLength` property.

### Concurrent PATCH Safety

The TUS protocol allows only one concurrent PATCH for a given file, but enforcing this across multiple app instances requires explicit locking. Use **`DistributedLockTusLockProvider`** (from `Headless.Tus.DistributedLocks`): it wraps `IDistributedLock` with `AcquireTimeout = TimeSpan.Zero` and auto-extending leases. Non-blocking — tusdotnet receives `false` from `Lock()` and returns `423 Locked` immediately, and a crashed holder's lease expires so the file is not stuck.

Select it with `tus.UseDistributedLocks()` inside `AddHeadlessTus`, with an `IDistributedLock` backend registered (Redis, SQL Server, …). Every `MapHeadlessTus` endpoint then uses it without further wiring. A single-node deployment can omit it and rely on tusdotnet's in-process lock.

The same lock covers more than PATCH on an upload endpoint: a completion handler runs under it, so without a cross-node lock two nodes can run the handler for one upload at once ([Completion handler](#completion-handler)).

The lock is best-effort: a holder can lose it without noticing (a process pause, a lock-backend failover) while a second PATCH proceeds. Both stores therefore also fence their writes. `TusAzureStore` conditions every block-list commit and metadata write on the blob ETag its request read, and `TusS3Store` conditions every write of an upload's state object on its ETag, so the stale PATCH's write fails instead of overwriting the other PATCH's data (see the **Write fencing** design constraints under [`Headless.Tus.Azure`](#headlesstusazure) and [`Headless.Tus.S3`](#headlesstuss3)).

> The store writes blocks to the upload blob without holding an Azure blob lease on it, so do **not** lock that blob directly — a lease on the upload blob would make the store's own writes fail with `412`. Coordinate PATCHes through `DistributedLockTusLockProvider` (a separate lock resource) instead.

---

## Headless.Tus

Root package of the TUS stack: the `AddHeadlessTus` registration, the upload endpoint layer over tusdotnet, the tenancy seam, CORS defaults for browser clients, and the expired-upload cleanup. It carries the shared `tusdotnet` dependency.

### API and behavior

- `services.AddHeadlessTus(Action<HeadlessTusSetupBuilder>)`: the one registration entry point
- `HeadlessTusSetupBuilder`: `UseStore<TStore>(store)` and `UseStore<TStore>(factory)`, `AddUploadEndpoint(...)`, `ConfigureExpiredUploadsCleanup(...)`, plus `RegisterStore<TStore>(...)` and `RegisterCrossCuttingExtension(...)` for a package that contributes a store or an add-on. `UseAzure`, `UseS3`, and `UseDistributedLocks` come from their packages
- `HeadlessTusUploadEndpointBuilder`: `OwnedBy(...)`, `ValidateWith<TValidator>()`, `OnCompleted<THandler>()`
- `TusUploadEndpointOptions`: one endpoint's limits and rules
- `app.MapHeadlessTus(pattern)` and `app.MapHeadlessTus(pattern, name)`: map a declared endpoint and return `IEndpointConventionBuilder`
- `ITusUploadValidator`, `TusUploadRequest`, `TusUploadValidationResult`: the application's own check before an upload starts
- `ITusUploadCompletedHandler`, `TusCompletedUpload`, `TusUploadHandoff`: the application's handler for a finished upload
- `ITusHandoffStore`, `TusHandoffState`: the optional store capability that records a handoff; `TusAzureStore` and `TusS3Store` implement it
- `tenancy.Tus(t => t.ScopeByTenant())` on the root tenancy builder (`HeadlessTusTenancyBuilder`)
- `TusCorsDefaults`: the tus 1.0.0 CORS surface as constants: `ExposedHeaders`, `AllowedHeaders`, `AllowedMethods` (includes the PATCH/DELETE that default CORS configs miss)
- `CorsPolicyBuilder.WithTusHeaders()`: applies all three in one call; origins and credentials stay the caller's decision
- `TusExpiredUploadsCleanupOptions`: tunes the job that removes expired incomplete uploads

### Design constraints

#### Registration

`AddHeadlessTus` selects the host's one store, declares the upload endpoints, and registers the cleanup job. It requires exactly one store. With none, with more than one, or on a second call for the same service collection it throws `InvalidOperationException` with a message that names the fix.

| Member | Store |
|---|---|
| `tus.UseAzure(...)` | `TusAzureStore` ([Headless.Tus.Azure](#headlesstusazure)) |
| `tus.UseS3(...)` | `TusS3Store` ([Headless.Tus.S3](#headlesstuss3)) |
| `tus.UseStore(store)`, `tus.UseStore(provider => ...)` | Any other `ITusStore`: tusdotnet's `TusDiskStore`, a store of your own, or a `TusAzureStore` or `TusS3Store` you construct by hand. The container does not dispose an instance you pass in. |

The selected store resolves from DI as `ITusStore` and as its own type, so application code can inject `TusS3Store`, `TusAzureStore`, or `ITusStore`. The capability interfaces (`ITusExpirationStore`, `ITusReadableStore`, `ITusTerminationStore`, and the rest) are not registered: cast the store. `tus.UseDistributedLocks()` adds cross-node file locks ([Headless.Tus.DistributedLocks](#headlesstusdistributedlocks)), and the cleanup job runs unless you turn it off ([Expired-upload cleanup](#expired-upload-cleanup)).

#### Upload endpoints

An upload endpoint is a set of rules declared inside `AddHeadlessTus` and mapped to a route.

- **Declare.** `tus.AddUploadEndpoint(options => ...)` declares the default endpoint and `tus.AddUploadEndpoint("videos", options => ...)` a named one. An application has at most one default endpoint and any number of named ones; names are compared by ordinal, and declaring one twice throws `InvalidOperationException`. Both forms also take an `IConfiguration` section or an `Action<TusUploadEndpointOptions, IServiceProvider>`. Each endpoint has its own `TusUploadEndpointOptions` instance (named options under the endpoint's name), validated when the host starts. [Configuration](#configuration) lists every option.
- **Shape.** The returned `HeadlessTusUploadEndpointBuilder` takes `OwnedBy(...)` ([Isolation](#isolation)), `ValidateWith<TValidator>()` any number of times ([Validation](#validation)), and `OnCompleted<THandler>()` once ([Completion handler](#completion-handler)). A validator or handler is named by type and resolved from the request's services each time it runs, so it may depend on scoped services. A type the application has not registered is registered as a scoped service.
- **Map.** `app.MapHeadlessTus("/files")` maps the default endpoint and `app.MapHeadlessTus("/videos", "videos")` a named one. An upload's URL is the pattern followed by its file id. The pattern may hold other route parameters, such as `/workspaces/{workspaceId}/videos`, but not one named `TusFileId`. The call returns `IEndpointConventionBuilder`, so `RequireAuthorization(...)`, `RequireCors(...)`, and other conventions attach to it. A declared endpoint serves nothing until it is mapped.
- **Mapping fails fast.** `MapHeadlessTus` throws `InvalidOperationException` when `AddHeadlessTus` was not called, when the endpoint was not declared, and when the endpoint has no `OwnedBy` function and no `ICurrentUser` is registered.

Every endpoint works against the host's one store, and an upload belongs to the endpoint that created it. tusdotnet serves each request with the endpoint's `MaxSize`, a sliding expiration of `Expiration`, the registered `ITusFileLockProvider` (tusdotnet's in-process lock when none is registered), and the tus extensions the store supports except concatenation. The endpoint neither advertises nor accepts parallel uploads: a request that carries `Upload-Concat` is answered with 404. The options are read on every request and are not stored with an upload, so a change to `MaxSize` or to an allow-list also applies to uploads already in progress.

#### Isolation

- **The stamp.** When an upload is created, the layer writes three server-owned entries after the client's `Upload-Metadata`: `headless-endpoint` (the endpoint's name; left out for the default endpoint), `headless-owner` (the owner key), and `headless-tenant` (the ambient `ICurrentTenant.Id`; left out when the request runs in no tenant). The tenant is recorded whether or not [tenant scoping](#tenant-scoping) is on. A client never sees the stamp: it is stripped from HEAD responses and from the metadata a completion handler receives. A client cannot supply it either: a creating request that sends a key starting with `headless-`, in any letter case, is refused with 400.
- **Not found for anyone else.** A request that names a file id is served only when the stamp's endpoint, owner, and tenant all equal the caller's. Any other caller gets 404 with the same headers and body as a request for an id that does not exist, so a file id reveals nothing. That covers another owner, another tenant, the same owner reaching the upload through another endpoint's route, a caller with no tenant asking for a tenant's upload, and an upload that carries no readable stamp, such as one created through a raw `MapTus` endpoint.
- **The owner key.** It is the result of the endpoint's `OwnedBy` function, which takes the `HttpContext` and may be asynchronous, or `ICurrentUser.UserId` when the endpoint has none. The function runs on every request to the endpoint, and keys are compared by ordinal. A request whose key is `null`, empty, or white space is refused with 401, whatever it asks for; only `OPTIONS` is answered without one. A creating request whose key is longer than 256 characters is refused with 400.
- **The layer trusts the key.** It does not check that the caller may act for the owner the function returns. With the default owner that is the signed-in user, which the authentication scheme already proved. With `OwnedBy(context => context.Request.RouteValues["workspaceId"]?.ToString())` the key is client-chosen input, and anyone who can reach the route can see, resume, and delete that workspace's uploads. The authorization policy on the mapped endpoint must prove the caller may use the key: `app.MapHeadlessTus("/workspaces/{workspaceId}/videos", "videos").RequireAuthorization("WorkspaceMember")`. Everyone who presents the same key shares its uploads.
- **Store failures.** When the store fails while the layer reads the stamp, a write or delete is answered with 503 and a status request with 423, never 404: the upload may be the caller's, and a tus client that is told an upload is gone abandons it.
- **Reading an upload outside the endpoint.** The stamp lives in the upload's own metadata, so code that reads an upload through the store, for example a job that calls `ITusReadableStore.GetFileAsync` and `ITusFile.GetMetadataAsync`, sees the `headless-` entries beside the client's. Filter them out or use them; do not show them to users as client metadata. The store applies none of the checks above: such code decides for itself whose upload it may touch.
- **Metadata budget.** The stamp is part of the stored `Upload-Metadata` string. On `TusAzureStore` that string is capped at about 7 KB, and the stamp takes its share: the owner entry alone can reach about 1 KB for a 256-character key, plus the endpoint name and tenant id. A creating request whose metadata no longer fits is refused with 400.

#### Validation

A creating request is checked before the store creates anything, in this order. The first rule that fails refuses the request, and nothing after it runs.

| Rule | Option | Refused with |
|---|---|---|
| The declared length is above the maximum size | `MaxSize` | 413 |
| A metadata key contains white space or a control character | always | 400 |
| A required metadata field is missing, or sent without a value | `RequiredMetadata` | 400, naming the field |
| No content type is declared, or the declared one is not in the list | `AllowedContentTypes` (when it has an entry) | 415 |
| No file name is declared, the name has no extension, or the extension is not in the list | `AllowedFileExtensions` (when it has an entry) | 415 |
| The declared length is zero | `AllowEmptyUploads` is `false` | 400 |
| An application validator returns a failure | `ValidateWith<TValidator>()` | the failure's status and message; 400 when it names none |

- The client declares the content type in the `filetype` metadata field, or in `type` when it sends no `filetype`; anything after a `;` is ignored. It declares the file name in `filename`, or in `name`. Content types and extensions compare without regard to letter case, an extension with or without its leading dot, and required metadata keys by exact case.
- A key such as `<tab>filetype` is refused because tusdotnet reads it as a key of its own while the stored metadata is written with each entry trimmed: the rules would check `type`, and the first-bytes check and the completion handler would later read the unchecked `filetype`.
- An upload whose length is deferred is not empty when it is created, so `AllowEmptyUploads` does not apply to it. `MaxSize` still does: the write that carries the upload past it is refused with 413.
- Validators run in the order they were declared, after the endpoint's own rules have passed. `ITusUploadValidator.ValidateAsync` receives a `TusUploadRequest` (`Length`, which is `null` for a deferred length, the client's `Metadata`, `TenantId`, `OwnerKey`, and the `HttpContext`) and returns `TusUploadValidationResult.Success`, `Failure(message)` (400), or `Failure(statusCode, message)` with a status from 400 to 599. An exception that leaves a validator fails the request with a server error; return a failure to refuse an upload. Everything in `Metadata` is client input and unverified.

**The first-bytes check.** What a client declares is only a claim. While the endpoint lists content types or extensions and `VerifyFileSignature` is `true` (the default), the upload's first write is also checked against the format its leading bytes show:

- A recognized format passes when the lists allow it, by its content type or its extension. An allowed extension also allows the format of its usual content type, so `jpeg` allows a file recognized as `jpg`. Any other recognized format is refused with 415.
- When nothing is recognized, the upload passes if its declared type has no signature, such as `text/csv`, and is refused with 415 if its declared content type or extension belongs to a format that has one.
- **Container formats are verified to their container only.** Office Open XML files (docx, xlsx, pptx) and legacy Office files (doc, xls, ppt) are identified by a directory elsewhere in the file, which the first write does not carry. An allowed docx, xlsx, or pptx therefore accepts any ZIP archive, and an allowed doc, xls, or ppt accepts any compound file. An endpoint that needs more opens the content in its completion handler and rejects there.
- **Very short signatures match text.** A few formats are recognized by two or three leading characters, for example `BM` (BMP), `MZ` (Windows executable), and `ID3` (MP3). A text upload that starts with those letters on an endpoint that allows only a text type, such as csv, is recognized as that format and answered with 415.
- **The first write must be long enough.** It must carry the bytes the check reads (a few hundred; the refusal names the exact count) or the whole upload when the upload is shorter. A shorter first write is refused with 400. A length that is still deferred counts as longer than the check needs, so a short first write on such an upload is refused too, unless that same request declares the length. A write with no bytes is not a first write. tus clients send the whole file or large chunks by default, so this matters only for a client configured with a tiny chunk size.
- **A refused upload is deleted**, for the 400 as well as the 415. When the store cannot delete, or the delete fails, the refusal stands and the upload stays until it expires.
- **Creation with upload answers 201, then 404.** When the refused bytes arrive in the creating request itself, tusdotnet still answers that request with 201 and `Upload-Offset: 0`. The upload is deleted once the response is sent, and the client's next request for its URL answers 404.

`AddHeadlessTus` registers the format inspector (`IFileFormatInspector`, the `FileSignatures` library the framework's form-file validators use) and `IMimeTypeProvider` with `TryAdd`, so the ones an application registered are kept.

#### Completion handler

`OnCompleted<THandler>()` names the application's `ITusUploadCompletedHandler` for an endpoint. The handler takes each finished upload over, for example by recording it or queueing its processing. `HandleAsync` receives a `TusCompletedUpload`: `FileId`, `Length`, the client's `Metadata` without the stamp, `TenantId`, `OwnerKey`, and `OpenContentAsync(cancellationToken)`, which opens a new stream over the content on a store that implements `ITusReadableStore` and throws `NotSupportedException` on one that does not.

The handler returns one of three results:

| Result | Effect |
|---|---|
| `TusUploadHandoff.Keep` | The upload is recorded as handed off and stays in the store. It is the application's from then on. |
| `TusUploadHandoff.Delete` | The upload is recorded as handed off and removed once the response is sent. Use it when the handler copied what it needs. If that removal fails, the failure is logged and the upload stays, handed off. |
| `TusUploadHandoff.Reject(message)` | The upload is removed at once, the request is answered with 400 and the message, and the handler never runs for it again. |

**The handler contract.**

- **It runs at least once, and can run more than once.** The handler runs inside the request that stored the upload's last byte. An upload counts as handed off only after the handler returns `Keep` or `Delete` and the store recorded it, so a handler that threw, a record that could not be written, and a request that died before the handler ran all leave the upload owed. Make the handler safe to repeat, for example by keying its work on `FileId`.
- **It can run on several nodes at once.** The handler runs under the upload's file lock. tusdotnet's default lock is per process, so two nodes can run the handler for one upload at the same time unless `UseDistributedLocks` gives them a shared lock.
- **Keep it short.** The client waits on it, and the upload stays locked while it runs. Record or queue the work and return.
- **It can be cancelled.** `cancellationToken` is the request's token, cancelled when the client abandons the request. A handler that stops on it by throwing has failed like any other: the upload stays owed.
- **An exception is a transient failure, not a refusal.** The request fails with a server error and the upload stays for the next attempt. Return `Reject` to refuse an upload for good.
- **Reject on the first run.** A handler that rejects only after it failed once reaches the reject on a status request. That request then fails with 400, and a tus client reacts to a failed status request by creating a new upload and sending the whole file again before it reports the refusal. Decide whether the upload is acceptable before doing anything that can fail.

**The re-run on S3 and Azure.** `TusS3Store` and `TusAzureStore` implement `ITusHandoffStore`, which records the handoff on the upload. There, a complete upload that is still owed gets the handler again on its caller's next status request (HEAD), which is the request a tus client sends after any failed write:

- The handler succeeds: the status request is answered as usual, with the offset equal to the length, and later status requests do not run the handler.
- The handler throws, or the store fails while the layer looks: the request is answered with 423, the one status a tus client asks the same URL again after.
- Another request holds the upload's lock: 423 as well.
- The handler returns `Delete`: the status request is answered first, then the upload is removed.
- The handler returns `Reject`: 400 with the message, and the upload is removed.

A status request for a complete upload on an endpoint with a handler takes the upload's lock and reads the handoff record from storage every time, handed off or not.

**Other stores.** On a store that does not implement `ITusHandoffStore`, such as tusdotnet's `TusDiskStore`, the handler runs once per upload, inside the request that completed it, and a failure is not repeated. `MapHeadlessTus` logs a warning at startup that names the endpoint and the store.

**Completion inside the creating request.** An upload can finish in the request that creates it: creation-with-upload that carries the whole file, or an empty upload on an endpoint that allows one. The handler runs in that request. If it throws, the layer deletes the upload and the request fails with a server error, because the client never learned the upload's URL and no later request could run the handler. The same holds when the handler returned `Delete` and the handoff could not be recorded. When the handler returned `Keep` and the handoff could not be recorded, the request also fails with a server error, but the upload stays in the store: the application already holds it. Nothing runs the handler for it again, since the client has no URL to ask with. In both cases the client was told the request failed after the handler accepted the upload. A client that sends the creating request again creates a new upload with a new id, and the handler is given the same content a second time. Being safe to repeat for one `FileId` does not cover this, so an application that must not hold the content twice compares it by its own key (a metadata field or a content hash) and deletes the earlier upload through the store. A `Reject` there answers 400 as usual.

**DELETE rules.** A client may delete its own upload while it is incomplete, and while it is complete and still owed; the handler does not run for an upload its uploader cancels. An upload whose handler returned `Keep` but whose handoff could not be recorded is still owed, so its client can delete it until a status request records the handoff: a handler that keeps an upload must tolerate finding it gone. Deleting a handed-off upload is refused with 403. On a store without `ITusHandoffStore`, every complete upload of an endpoint that has a handler counts as handed off. On an endpoint with no handler nothing is ever handed off, and a client may delete any of its uploads.

**What stays in the store.**

- **Handed-off uploads never expire and are the application's to delete.** The expiry window covers incomplete uploads only. Delete a kept upload through the store when the application is done with it (`ITusTerminationStore.DeleteFileAsync`), inside the upload's tenant when uploads are [scoped by tenant](#tenant-scoping).
- **A complete upload whose client never asks again stays unhandled.** No background pass re-runs handlers: the re-run needs a status request. tus-js-client gives up after its retries (four by default), and a closed tab never sends the request. Such an upload holds all of its bytes, is not handed off, does not expire, and the application never hears of it. A client that drops the connection at the last byte and never asks again can produce one on demand.
- **A rejected upload on a store that cannot delete** (no `ITusTerminationStore`) stays where it is.

#### Tenant scoping

An upload's stamp records the ambient tenant whenever the request runs in one, and a caller in another tenant, or in none, gets 404 ([Isolation](#isolation)). That holds with no further setup. `ScopeByTenant()` on the tenancy seam adds two things: a tenant becomes mandatory, and the Headless stores keep each tenant's uploads apart physically.

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.Tus(tus => tus.ScopeByTenant()));
```

- **Answers.** A creating request with no ambient tenant is refused with 403 and creates nothing. So is one whose tenant id cannot be a storage path segment: the id must start and end with a lowercase ASCII letter or digit and hold only lowercase ASCII letters, digits, `.`, `_`, and `-` (`TenantStorageSegment.IsValid`, the rule blob tenancy applies). A request that names a file id with no tenant, or with such an id, is answered with 404, like any request for an upload that is not the caller's.
- **Layout.** The stores registered through `UseS3` and `UseAzure` put a reserved `.tenants/{tenant}/` segment in front of the file id. In S3 that is `{Prefix}.tenants/{tenant}/{id}` for the finished object, `{Prefix}.tus/.tenants/{tenant}/{id}/` for the working set, `{Prefix}.tus-completed/.tenants/{tenant}/{id}` for the completed record, and `{Prefix}.tus-handed-off/.tenants/{tenant}/{id}` for the handed-off mark. In Azure the blob is `{BlobPrefix}.tenants/{tenant}/{id}`, for example `uploads/.tenants/acme/{id}`. A file id cannot start with `.`, so a tenant's keys never collide with an upload kept outside any tenant. The bucket or container is shared: there is no bucket or container per tenant.
- **The sweep and purge cover every tenant.** The expired-upload cleanup and `resources purge` run with no ambient tenant and cover the uploads kept outside any tenant and those of every tenant.
- **Code outside a request.** A scoped store asks for the ambient tenant on every call. A job that reads or deletes an upload must run inside `ICurrentTenant.Change(tenantId)` with the upload's tenant; without one the store throws `MissingTenantContextException`.
- **Stores the seam does not reach.** A `TusS3Store` or `TusAzureStore` constructed by hand, and any store passed to `UseStore`, keeps its own layout with every tenant's uploads side by side. Its uploads are bound to their tenant by the stamp alone, which the endpoint enforces and the store does not.
- **Switching it on later.** Scoping does not move the uploads a store already holds. The S3 and Azure stores keep them outside any tenant, where a request no longer finds them (404). The incomplete ones are removed by the cleanup once they expire; the complete ones stay until the application deletes them.
- **Setup.** The order of `AddHeadlessTus` and the tenancy call does not matter. The seam is reported in the tenant posture manifest as `Tus` with the `scope-by-tenant` capability, and startup fails with `HEADLESS_TENANCY_TUS_NO_STORE` when it is recorded and `AddHeadlessTus` was never called. [Multi-Tenancy](multi-tenancy.md) covers how a request gets its tenant.

#### Request body limits

A tus client may send a whole upload in one PATCH, and tus-js-client and Uppy do by default. tusdotnet leaves the server's request body limit alone, and Kestrel's default is 30 MB, so `MapHeadlessTus` sets the limit to the endpoint's `MaxSize` on every request to the endpoint. That replaces whatever limit the application set for the server, higher or lower. It is skipped on a server that exposes no such limit or no longer lets it change.

Limits in front of the application cannot be lifted from code. A reverse proxy (nginx `client_max_body_size`, 1 MB by default), a gateway or CDN, and IIS request filtering (`maxAllowedContentLength`, about 28.6 MB by default) each refuse a larger body before the request reaches the endpoint. Raise them in their own configuration, or make clients send smaller PATCH requests (`chunkSize` in tus-js-client and Uppy) that stay below the lowest limit on the path.

#### Response statuses

Every refusal by the layer is a tus response with a plain-text body. It writes no `ProblemDetails`.

| Status | When |
|---|---|
| 400 | A required metadata field is missing; an empty upload on an endpoint that does not allow one; a client metadata key that starts with `headless-`; an owner key longer than 256 characters; a first write too short for the first-bytes check; a completion handler's `Reject`; a validator failure that names no status. |
| 401 | The request has no owner key. |
| 403 | Tenant scoping is on and the creating request has no tenant, or a tenant id that cannot be a path segment; a client's DELETE of a handed-off upload. |
| 404 | The file id does not exist, or its upload belongs to another endpoint, owner, or tenant; the upload carries no stamp; the upload expired; the request carries `Upload-Concat`. |
| 413 | The declared length, or the bytes that arrive, exceed `MaxSize`. |
| 415 | The declared content type or file extension is missing or not allowed; the first bytes are not of an allowed format, or not of the type the upload declares. |
| 423 | Another request holds the upload's lock; a status request found a completion handler still owed and the handler failed; the store failed while a status request was being authorized. |
| 503 | The store failed while a write or delete was being authorized. |

A validator can answer with any status from 400 to 599. Three outcomes are not refusals with a message: an exception from a completion handler or a validator fails the request with a server error (500 unless the application's exception handling says otherwise), a refused creation-with-upload answers 201 and then 404 ([Validation](#validation)), and tusdotnet's own protocol errors keep their statuses.

#### Raw MapTus

tusdotnet's `MapTus` still works beside `MapHeadlessTus`, for an endpoint whose configuration the application builds by hand. It gets none of the layer's checks:

- No stamp and no owner or tenant check: anyone who can reach the route and holds a file id can read the state of, resume, or delete that upload.
- No size limit, validation, or first-bytes check beyond what the `DefaultTusConfiguration` itself sets, and no completion handler beyond tusdotnet's `OnFileCompleteAsync` event, which runs once.
- No lock provider and no request body limit unless the factory sets them: without `FileLockProvider` tusdotnet locks within the process only, and Kestrel's 30 MB body limit applies.
- Concatenation stays available when the configuration allows it.

The two do not mix. An upload created through `MapTus` carries no stamp, so it is "not found" on every `MapHeadlessTus` endpoint. The reverse is not true: a raw endpoint over the same store serves an upload a `MapHeadlessTus` endpoint created to anyone who holds its id, and echoes the `headless-` entries in HEAD responses. Do not map both over one store unless that is intended. The registered store's own behavior is unchanged on a raw endpoint: with tenant scoping on, a `UseS3` or `UseAzure` store still keeps the upload under the request's tenant and throws `MissingTenantContextException` for a request with none.

```csharp
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

app.MapTus(
    "/raw-files",
    httpContext => Task.FromResult(new DefaultTusConfiguration
    {
        // The store AddHeadlessTus registered.
        Store = httpContext.RequestServices.GetRequiredService<ITusStore>(),
        // Everything else is yours to set on a raw endpoint.
        MaxAllowedUploadSizeInBytesLong = 100L * 1024 * 1024,
        Expiration = new SlidingExpiration(TimeSpan.FromHours(1)),
        // Registered by UseDistributedLocks; leave it unset on a single node.
        FileLockProvider = httpContext.RequestServices.GetRequiredService<ITusFileLockProvider>(),
    }));
```

#### Expired-upload cleanup

tusdotnet only reports `Upload-Expires`; nothing removes an expired upload unless a job does. `AddHeadlessTus` registers that job against the host's store, and it runs by default: one pass at startup, which reclaims uploads that expired while the application was down, then one pass per `Interval`. `tus.ConfigureExpiredUploadsCleanup(options => ...)` changes the interval, or sets `Enabled = false` on a host that should not sweep, for example when a dedicated worker runs the job for the whole deployment. The job does nothing on a store that does not implement `ITusExpirationStore`. A pass that fails, for example because the bucket or container is not provisioned yet, is logged as an error, and the next tick tries again.

Cleanup targets **incomplete** uploads only: the Headless stores never report completed uploads as expired, so the job cannot destroy finished data. The default 5-minute interval trades reclaim latency against the store scan each pass performs. In multi-node deployments every node runs its own loop against the same store. With `tus.UseDistributedLocks()` selected, a node runs a pass only if it takes the cleanup lease (`{resourcePrefix}:expired-uploads-cleanup`, zero wait), and the lease stays held for 90% of the interval after the pass, so the store is swept about once per interval across the cluster and a node that loses the race skips its tick. The lease expires on its own, so a crashed node delays cleanup by at most one interval. Without the lock provider every node sweeps on every tick: deletions are idempotent so this is safe, but the scan load multiplies and the logged removal counts are per-node.

#### Default content type

The default headers provider of each store (`DefaultTusAzureBlobHttpHeadersProvider`, `DefaultTusS3ObjectHeadersProvider`) stores every upload as `application/octet-stream` and ignores the `filetype` metadata.

The client chooses `filetype`. An object served from the bucket's or CDN's origin as `text/html`, `image/svg+xml`, or another type a browser renders or runs would execute the uploader's script in that origin (stored XSS). Which types are safe depends on where the app serves its uploads from, so the framework does not decide it. A browser downloads an `application/octet-stream` object instead of displaying it.

An app that wants the real type implements the store's headers provider: allow-list the types it serves instead of storing `filetype` as sent, and serve anything a browser can run from an isolated origin or with `Content-Disposition: attachment`.

### Install

```bash
dotnet add package Headless.Tus
```

Pulled in transitively by every `Headless.Tus.*` provider package.

### Setup and use

Two named endpoints over one S3 store, one owned by a workspace and one by the signed-in user:

```csharp
using Headless.Tus;

var builder = WebApplication.CreateBuilder(args);

// The IAmazonS3 registration is the app's own: see Headless.Tus.S3.

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseS3(options => options.Bucket = "tus-uploads");
    tus.UseDistributedLocks(); // Headless.Tus.DistributedLocks, for more than one node

    tus.AddUploadEndpoint("videos", options =>
        {
            options.MaxSize = 2L * 1024 * 1024 * 1024;
            options.AllowedContentTypes.Add("video/mp4");
            options.AllowedFileExtensions.Add("mp4");
            options.RequiredMetadata.Add("filename");
        })
        .OwnedBy(context => context.Request.RouteValues["workspaceId"]?.ToString())
        .ValidateWith<VideoQuotaValidator>()
        .OnCompleted<VideoUploadHandler>();

    // No OwnedBy: the signed-in user owns the upload, read from ICurrentUser,
    // which builder.AddHeadless() registers.
    tus.AddUploadEndpoint("avatars", builder.Configuration.GetSection("Uploads:Avatars"));
});

var app = builder.Build();

// The layer trusts the owner key, so the policy must prove the caller belongs to the workspace.
app.MapHeadlessTus("/workspaces/{workspaceId}/videos", "videos").RequireAuthorization("WorkspaceMember");
app.MapHeadlessTus("/avatars", "avatars").RequireAuthorization();

app.Run();
```

```json
{
  "Uploads": {
    "Avatars": {
      "MaxSize": 5242880,
      "Expiration": "01:00:00",
      "AllowedContentTypes": ["image/png", "image/jpeg"],
      "AllowedFileExtensions": ["png", "jpg", "jpeg"]
    }
  }
}
```

The validator and the handler. Neither needs a registration of its own; the services they depend on are the application's:

```csharp
using Headless.Tus;
using Microsoft.AspNetCore.Http;

public interface IStorageQuota
{
    ValueTask<bool> HasRoomAsync(string ownerKey, long bytes, CancellationToken cancellationToken);
}

public sealed class VideoQuotaValidator(IStorageQuota quota) : ITusUploadValidator
{
    public async ValueTask<TusUploadValidationResult> ValidateAsync(
        TusUploadRequest request,
        CancellationToken cancellationToken)
    {
        // Length is null when the client defers it: count such an upload at the endpoint's maximum.
        var bytes = request.Length ?? 2L * 1024 * 1024 * 1024;

        return await quota.HasRoomAsync(request.OwnerKey, bytes, cancellationToken)
            ? TusUploadValidationResult.Success
            : TusUploadValidationResult.Failure(
                StatusCodes.Status403Forbidden,
                "The workspace is over its storage quota.");
    }
}

public interface IVideoCatalog
{
    Task RecordAsync(
        string fileId,
        string ownerKey,
        string? tenantId,
        string fileName,
        long length,
        CancellationToken cancellationToken);
}

public sealed class VideoUploadHandler(IVideoCatalog catalog) : ITusUploadCompletedHandler
{
    public async ValueTask<TusUploadHandoff> HandleAsync(
        TusCompletedUpload upload,
        CancellationToken cancellationToken)
    {
        // Decide before anything that can fail: a reject reached on a re-run costs the client a second upload.
        if (!upload.Metadata.TryGetValue("filename", out var fileName) || fileName.Length == 0)
        {
            return TusUploadHandoff.Reject("The upload has no file name.");
        }

        // Safe to repeat: the catalog keys its record on the file id.
        await catalog.RecordAsync(
            upload.FileId,
            upload.OwnerKey,
            upload.TenantId,
            fileName,
            upload.Length,
            cancellationToken);

        // The upload stays in the store and is the application's to delete from here on.
        return TusUploadHandoff.Keep;
    }
}
```

Working on a kept upload later, outside the endpoint. The store is injected as its own type, and the upload's tenant is entered first because a tenant-scoped store needs it:

```csharp
using Headless.MultiTenancy;
using Headless.Tus;

public sealed class VideoProcessor(TusS3Store store, ICurrentTenant currentTenant)
{
    public async Task ProcessAsync(string fileId, string? tenantId, CancellationToken cancellationToken)
    {
        using (currentTenant.Change(tenantId))
        {
            var file = await store.GetFileAsync(fileId, cancellationToken)
                ?? throw new InvalidOperationException($"Upload {fileId} is no longer in the store.");

            // GetMetadataAsync returns the client's entries and the server's headless-* entries.
            await using (var content = await file.GetContentAsync(cancellationToken))
            {
                // Read the content.
            }

            // Handed-off uploads never expire: delete the upload when the application is done with it.
            await store.DeleteFileAsync(fileId, cancellationToken);
        }
    }
}
```

tusdotnet's disk store, for a single node or local development. It does not record a handoff, so a completion handler runs once per upload, and it has no per-tenant layout:

```csharp
using Headless.Tus;
using tusdotnet.Stores;

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseStore(new TusDiskStore("/var/lib/app/uploads"));
    tus.AddUploadEndpoint(options => options.MaxSize = 100L * 1024 * 1024);
});
```

CORS for browser clients on another origin:

```csharp
using Headless.Tus;

builder.Services.AddCors(options =>
    options.AddPolicy("tus", policy =>
        policy.WithOrigins("https://app.example.com").WithTusHeaders()));
```

### Configuration

`TusUploadEndpointOptions`, one instance per endpoint, validated at startup:

| Option | Default | Notes |
|---|---|---|
| `MaxSize` | none (required) | The largest upload the endpoint accepts, in bytes. Must be positive. A larger declared length, or a deferred-length upload that grows past it, is refused with 413. Also the request body limit of the endpoint's requests. |
| `Expiration` | `24 hours` | How long an incomplete upload stays resumable after its last write (sliding). Past it the upload is answered as not found and removed by the cleanup. Must be positive. A completed upload never expires on the Headless stores, and a store without expiration support ignores the value. |
| `AllowedContentTypes` | empty (every type) | Content types an upload may declare, such as `image/png`. With an entry, a creating request that declares none, or one outside the list, is refused with 415, and the first bytes are checked. |
| `AllowedFileExtensions` | empty (every extension) | File name extensions an upload may declare, such as `png`, with or without the dot. With an entry, a creating request with no file name, no extension, or one outside the list is refused with 415, and the first bytes are checked. Unrelated to the tus protocol extensions. |
| `RequiredMetadata` | empty | Metadata keys every upload must carry with a value. A missing one is refused with 400. |
| `AllowEmptyUploads` | `false` | Whether a declared length of zero is accepted (400 otherwise). |
| `VerifyFileSignature` | `true` | Whether the first bytes are checked against the two allow-lists. No effect while both are empty. |

No list may hold a blank entry. [Validation](#validation) describes each rule.

`TusExpiredUploadsCleanupOptions`, set through `tus.ConfigureExpiredUploadsCleanup(options => ...)`:

| Option | Default | Notes |
|---|---|---|
| `Enabled` | `true` | Whether this host runs the cleanup job. |
| `Interval` | `5 minutes` | How often expired incomplete uploads are removed. Each pass scans the store's uploads, so prefer a coarser interval for a large container or bucket. Must be positive. |

### Runtime behavior

- `AddHeadlessTus` registers the selected store as a singleton under `ITusStore` and its own type, the upload endpoints' options with their validator, an internal hosted service for the cleanup, `TimeProvider.System` (TryAdd), and `IFileFormatInspector` and `IMimeTypeProvider` (TryAdd). It registers each validator and handler type the application has not registered, as scoped.
- `MapHeadlessTus` resolves the store when the endpoint is mapped, and logs the run-once warning for an endpoint whose handler the store cannot re-run.
- `tenancy.Tus(t => t.ScopeByTenant())` registers the default `ICurrentTenant` services when the host has none.
- `TusCorsDefaults` and `WithTusHeaders` are pure helpers with no registrations.

---

## Headless.Tus.Azure

Azure Blob Storage TUS store implementation.

### API and behavior

- `TusAzureStore` — full `ITusStore` implementation backed by Azure block blobs; also implements `ITusPipelineStore` for zero-copy `PipeReader`-based ingestion
- TUS extensions supported:
  - **Creation** (`ITusCreationStore`) — `CreateFileAsync`, `GetUploadMetadataAsync`
  - **CreationDeferLength** (`ITusCreationDeferLengthStore`) — `SetUploadLengthAsync`
  - **Concatenation** (`ITusConcatenationStore`) — server-side block copy with streaming fallback for emulators
  - **Expiration** (`ITusExpirationStore`) — `SetExpirationAsync`, `GetExpiredFilesAsync`, `RemoveExpiredFilesAsync`
  - **Checksum** (`ITusChecksumStore`) — SHA1, SHA256, SHA512, MD5; constant-time comparison; deferred block commit
  - **Termination** (`ITusTerminationStore`) — `DeleteFileAsync`
  - **Readable** (`ITusReadableStore`) — `GetFileAsync` returns `ITusFile`
- **Handoff** (`ITusHandoffStore`) — records that a completed upload was handed off to the application, so an upload endpoint can run a failed completion handler again ([Completion handler](#completion-handler))
- `tus.UseAzure(...)` — overloads: `IConfiguration`, `Action<TusAzureStoreOptions>`, `Action<TusAzureStoreOptions, IServiceProvider>`
- `ITusAzureBlobHttpHeadersProvider` / `DefaultTusAzureBlobHttpHeadersProvider` — per-file HTTP header customization (Content-Type, cache control); the default sets only `Content-Type`, by the [shared rule](#default-content-type)
- Adaptive chunk sizing: automatic selection between `BlobDefaultChunkSize` (4 MB) and `BlobMaxChunkSize` (16 MB default; up to 100 MB) based on declared upload size
- Pooled buffer stream splitting via `ArrayPool<byte>` to minimize allocations during PATCH ingestion

### Design constraints

**Block blob chunking and checksum deferral.** TUS Checksum verification happens _after_ all PATCH data is staged. `TusAzureStore` stages blocks during `AppendDataAsync` but defers the commit list until `VerifyChecksumAsync` succeeds. The staged block range — a constant-size `token:firstIndex:count` triple that reconstructs the exact block IDs at commit time — and the pre-calculated checksum are written to blob metadata (`tus_last_chunk_blocks`, `tus_last_chunk_checksum`); being constant-size, the tracking cannot approach Azure's 8 KB blob-metadata cap no matter how many blocks one PATCH stages. On success, blocks are committed atomically together with metadata update. On mismatch, blocks are left uncommitted and Azure auto-purges them within 7 days. This means `GetUploadOffsetAsync` reads committed block sizes — not the blob `ContentLength` property — to report accurate offset to resuming clients.

**Blob HTTP header preservation.** Headers returned by `ITusAzureBlobHttpHeadersProvider` are applied at creation and re-supplied on every block-list commit — appends, checksum-verified commits, and rollbacks. Azure's Put Block List clears any `x-ms-blob-*` property omitted from the request, so without the re-supply a custom content type or cache control set at creation would silently reset to `application/octet-stream` on the first PATCH. `ContentHash` is deliberately not carried over: the content changes with each commit, so echoing the stored MD5 would persist a stale digest.

**Write fencing.** Every block-list commit and metadata write — append commits, checksum staging and verified commits, rollbacks and discards, `SetUploadLengthAsync`, and `SetExpirationAsync` — sends the ETag the calling request read as `If-Match` and carries each write's returned ETag into its next write, so a request's own sequential writes keep matching while a request whose view is stale gets `412`. A stale writer fails with `Azure.RequestFailedException` (status `412`), not `TusStoreException`, so tusdotnet does not answer `400` and tus clients retry and resume (see Agent Rules). `SetExpirationAsync` is the exception that retries: it merges one field into the blob's current metadata, so it re-reads and re-applies up to three times rather than failing a PATCH whose data already committed. Each store call fences from its own read: in the checksum header flow, `VerifyChecksumAsync` re-reads the blob, and its digest comparison is what rejects blocks a different writer staged in between. The concatenated final blob is committed with `If-None-Match: *`, so a new final upload can never replace an existing blob. A tus `DELETE` and the post-concatenation deletion of partials stay unconditional (the client asked for removal, and a concatenated partial is consumed whatever its state); the expired-upload reaper's deletes are conditional (see Agent Rules).

**Server-side concatenation.** Final-file creation uses `StageBlockFromUri` to copy block ranges across blobs without moving data through the application server. When server-side copy is unavailable — Azurite returns HTTP 501, and a private container returns HTTP 403 because the source blob is not readable via its bare URI (no SAS) — the store falls back to download-then-re-upload streaming automatically.

**Container provisioning.** Creating the container is a deploy step, not store construction: the `AzureBlob` provisioner that `UseAzure` registers creates it on `resources provision`, so the running host needs no container-create permission. At start the host verifies the container with one existence check (`ResourceStartupCheck.GateReadiness`); override it per kind with `ResourceProvisioningOptions.StartupChecks`.

**Handed-off record.** `MarkHandedOffAsync` writes a `tus_handed_off` entry into the blob's metadata under the blob's ETag, so it fails with `412` instead of overwriting another request's write; an upload endpoint treats that as a failed handler run and runs the handler again. Every later metadata write carries the entry along, and deleting the upload removes it with the blob.

**Tenant layout.** A store registered through `UseAzure` names an upload's blob `{BlobPrefix}{id}`, or `{BlobPrefix}.tenants/{tenant}/{id}` once the application scopes uploads by tenant ([Tenant scoping](#tenant-scoping)). The expiry pass lists the whole prefix once and takes each upload's tenant from the blob's name, so it needs no ambient tenant, and it skips a blob whose name the store could not have produced. `resources purge` deletes everything under `BlobPrefix`, every tenant's uploads included.

### Install

```bash
dotnet add package Headless.Tus.Azure
```

### Setup and use

Register a `BlobServiceClient`, then select the store inside `AddHeadlessTus`:

```csharp
using Azure.Storage.Blobs;
using Headless.Tus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]));

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseAzure(options =>
    {
        options.ContainerName = "uploads";
        options.BlobPrefix = "tus/";
    });

    tus.AddUploadEndpoint(options => options.MaxSize = 1024L * 1024 * 1024);
});

var app = builder.Build();

// No OwnedBy on the endpoint: the signed-in user owns each upload (ICurrentUser).
app.MapHeadlessTus("/files").RequireAuthorization();

app.Run();
```

Create the container as a deploy step before the host serves uploads:

```bash
dotnet MyApp.dll resources provision -k AzureBlob
```

A store constructed by hand goes through `UseStore`. It has no container provisioner, so create its container beforehand, and it keeps every upload outside any tenant, whatever the tenancy setup says:

```csharp
using Azure.Storage.Blobs;
using Headless.Tus;

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseStore(provider => new TusAzureStore(
        provider.GetRequiredService<BlobServiceClient>(),
        new TusAzureStoreOptions { ContainerName = "uploads", BlobPrefix = "tus/" }));

    tus.AddUploadEndpoint(options => options.MaxSize = 1024L * 1024 * 1024);
});
```

For more than one node, add `tus.UseDistributedLocks()` ([Headless.Tus.DistributedLocks](#headlesstusdistributedlocks)). A single-node deployment can omit it and rely on tusdotnet's in-process lock.

With custom HTTP headers per upload:

```csharp
using System.Collections.Frozen;
using Azure.Storage.Blobs.Models;
using Headless.Tus;

public sealed class MyHeadersProvider : ITusAzureBlobHttpHeadersProvider
{
    // The client chooses "filetype", so store it only when it is a type this app serves safely.
    private static readonly FrozenSet<string> _ServedTypes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "image/png",
        "image/jpeg",
        "application/pdf"
    );

    public ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata)
    {
        var contentType = metadata.TryGetValue("filetype", out var fileType) && _ServedTypes.Contains(fileType)
            ? fileType
            : "application/octet-stream";

        return ValueTask.FromResult(new BlobHttpHeaders
        {
            ContentType = contentType,
            CacheControl = "private, max-age=3600",
        });
    }
}

// UseAzure keeps a provider the application registered.
builder.Services.AddSingleton<ITusAzureBlobHttpHeadersProvider, MyHeadersProvider>();
```

### Configuration

`TusAzureStoreOptions` — all properties have defaults:

| Option | Default | Notes |
|---|---|---|
| `ContainerName` | `"tus-uploads"` | Azure Blob container name. |
| `BlobPrefix` | `"uploads/"` | Prefix applied to all blob names in the container. |
| `ContainerPublicAccessType` | `PublicAccessType.None` | Access type `resources provision` applies when it creates the container; ignored for an existing one. |
| `EnableChunkSplitting` | `true` | Splits large PATCH bodies into multiple Azure blocks. |
| `BlobDefaultChunkSize` | `4 MB` | Default block size for medium uploads. Must be 1 byte–100 MB and not exceed `BlobMaxChunkSize`. |
| `BlobMaxChunkSize` | `16 MB` | Block size used for uploads ≥ 100 MB; also the per-request memory buffering unit for large uploads. Max 100 MB — a store-imposed memory cap; Azure's own per-block limit is 4,000 MiB. |
| `DeletePartialFilesOnConcat` | `false` | Delete partial uploads after a final upload is committed (best-effort; keep `false` if clients reuse partials). |

Chunk size selection logic: uploads < 10 MB use `min(BlobDefaultChunkSize, fileSize)`; uploads 10–100 MB use `BlobDefaultChunkSize`; uploads ≥ 100 MB use `BlobMaxChunkSize`.

### Runtime behavior

- Construction makes no network call; the container must already exist (see Container provisioning).
- `UseAzure` registers `TusAzureStore` as a singleton (it requires a `BlobServiceClient` in DI), `TimeProvider.System` (TryAdd), the default `ITusAzureBlobHttpHeadersProvider` (TryAdd), and the `AzureBlob` container provisioner. Options run through FluentValidation with `ValidateOnStart`. A store constructed by hand and passed to `UseStore` gets none of these.
- For cross-node PATCH locking, add `tus.UseDistributedLocks()` from `Headless.Tus.DistributedLocks`.

---

## Headless.Tus.S3

TUS store for Cloudflare R2, Amazon S3, and S3-compatible servers such as MinIO. It writes each upload as one S3 multipart upload and takes the S3 client from the app.

### API and behavior

- `TusS3Store` — `ITusStore` implementation over the S3 API; also implements `ITusPipelineStore`. The `PipeReader` and `Stream` appends store the same offsets with the same durability.
- TUS extensions supported:
  - **Creation** (`ITusCreationStore`), including creation-with-upload
  - **CreationDeferLength** (`ITusCreationDeferLengthStore`), up to a capped length (see **Part size and length limits**)
  - **Concatenation** (`ITusConcatenationStore`) — partials of any size; parts inside one partial are copied server-side with a ranged `UploadPartCopy`
  - **Expiration** (`ITusExpirationStore`)
  - **Termination** (`ITusTerminationStore`) — removes the multipart upload, the state object, the tail segments, the finished object, the completed record, and the handed-off mark
  - **Readable** (`ITusReadableStore`) — `GetFileAsync` returns `ITusFile`
- **Handoff** (`ITusHandoffStore`) — records that a completed upload was handed off to the application, so an upload endpoint can run a failed completion handler again ([Completion handler](#completion-handler))
- **Checksum is not supported.** `TusS3Store` does not implement `ITusChecksumStore`, so tusdotnet does not advertise the extension. Use `Headless.Tus.Azure` when clients must send `Upload-Checksum`.
- `tus.UseS3(...)` — overloads: `IConfiguration`, `Action<TusS3StoreOptions>`, `Action<TusS3StoreOptions, IServiceProvider>`. It resolves `IAmazonS3` from DI with `GetRequiredService`, honors an `ITusS3ObjectHeadersProvider` or `ITusFileIdProvider` the application registers, adds `IHttpContextAccessor`, and registers the bucket as a provisioned resource of kind `S3Bucket`.
- `new TusS3Store(s3, options, objectHeadersProvider, fileIdProvider, loggerFactory, timeProvider, httpContextAccessor)` — every parameter after `options` is optional. The constructor validates the options and throws `ValidationException` on invalid ones. Select a store constructed this way with `tus.UseStore(store)`; it has no bucket provisioner and keeps every upload outside any tenant.
- `TusS3Provider` — `Aws` (default), `S3Compatible`, `R2`; selects the per-request settings (see **Providers and the S3 client**).
- `ITusS3ObjectHeadersProvider` / `DefaultTusS3ObjectHeadersProvider` / `TusS3ObjectHeaders` — the `Content-Type`, `Cache-Control`, `Content-Disposition`, `Content-Encoding`, and `Content-Language` the finished object carries.

### Design constraints

**Providers and the S3 client.** The app registers `IAmazonS3`; the store never builds one. Client-level settings are the app's to set, and registration does not check them:

| Server | `ServiceURL` | `ForcePathStyle` | `AuthenticationRegion` | Checksum settings | `Provider` |
|---|---|---|---|---|---|
| Cloudflare R2 | `https://<account-id>.r2.cloudflarestorage.com` | `true` | `auto` | `WHEN_REQUIRED` (required) | `R2` |
| Amazon S3 | SDK default (region endpoint) | SDK default | SDK default | SDK default | `Aws` |
| MinIO or another S3-compatible server | the server URL, `http` allowed | `true` | the server's region, often `us-east-1` | `WHEN_REQUIRED` (most servers reject the SDK's default CRC checksums) | `S3Compatible` |

R2 rejects the CRC checksums AWS SDK v4 adds to every request by default, so an R2 client must set `RequestChecksumCalculation` and `ResponseChecksumValidation` to `WHEN_REQUIRED`. `Provider` changes only per-request settings. Every object and part write sends a plain `Content-Length` body (`UseChunkEncoding = false`) for all three. `R2` sends unsigned payloads (`DisablePayloadSigning = true`), which skips a hash pass over every part. `S3Compatible` keeps payloads signed, because the SDK refuses unsigned payloads over plain `http`. `Aws` keeps the SDK's signing default. `R2` also sets the default `MultipartUploadLifetime` to 7 days.

Set the client's `Timeout` and `MaxErrorRetry` deliberately. Completion, the expiry refresh after a PATCH, and the sweep's removals run on `CancellationToken.None`, so a client disconnect or host shutdown cannot interrupt them halfway; the client's timeout and retry count are then the only bound on how long one of those calls waits on a slow or unreachable server.

R2 behavior is tested against a real R2 bucket in the release checks. Amazon S3 behavior is verified against LocalStack, not a real AWS account. MinIO is supported as an S3-compatible endpoint; its upstream repository was archived in April 2026.

**Object layout.** The store owns every key under `Prefix` (default `uploads/`), so give it a prefix no other writer uses: `resources purge` deletes objects there. An upload with file id `{id}` uses:

- `{Prefix}{id}` — the finished object, written when the upload completes.
- `{Prefix}.tus/{id}/info` — the JSON state object of an incomplete upload: length, part size, metadata, expiry, multipart upload id, committed parts and tail segments.
- `{Prefix}.tus/{id}/seg/...` — tail segments: received bytes that do not yet fill a part.
- `{Prefix}.tus-completed/{id}` — the record a completed upload keeps beside its finished object.
- `{Prefix}.tus-handed-off/{id}` — the mark of an upload that was handed off to the application. It is an object of its own, written only if it does not exist yet, because a completion that runs again rewrites the completed record. Deleting the upload removes it.

Once the application scopes uploads by tenant, a store registered through `UseS3` puts `.tenants/{tenant}/` in front of the file id in each of the four: `{Prefix}.tenants/{tenant}/{id}`, `{Prefix}.tus/.tenants/{tenant}/{id}/...`, `{Prefix}.tus-completed/.tenants/{tenant}/{id}`, and `{Prefix}.tus-handed-off/.tenants/{tenant}/{id}` ([Tenant scoping](#tenant-scoping)).

The tus metadata lives in the state object, not in S3 object metadata, so S3's 2 KB user-metadata limit does not apply. File ids become keys: the store rejects an id that starts with `.`, contains `/`, `\`, or a control character, or starts or ends with whitespace, with a `TusStoreException`.

**Write fencing.** Creation writes the state object with `If-None-Match: *`, and every later write sends `If-Match` with the ETag the request read. A request whose view is stale (it lost its file lock and another request wrote meanwhile) fails with `AmazonS3Exception` (`412 PreconditionFailed`, `409 ConditionalRequestConflict`, or `404 NoSuchKey`), deliberately not a `TusStoreException`. tusdotnet answers `500`, and the tus client resumes from HEAD. Do not map that exception to `400` or another non-retryable 4xx. Before uploading parts, a PATCH reserves a window of part numbers (`PartClaimWindow`) with one conditional state write, so a stale writer fails before it touches a part. This matters on R2, which replaces a part uploaded again under the same number and loses it when that upload fails. No step relies on a conditional delete.

**Part size and length limits.** S3 and R2 allow 10,000 parts per multipart upload, and R2 requires every part except the last to be the same size. The store fixes each upload's part size at creation, and every part except the last is exactly that size. Four claim windows of part numbers stay in reserve for crash recovery, so an upload spans at most `10,000 − 4 × PartClaimWindow` parts: 9,744 at the default window of 64.

- **Known length.** The part size is the larger of `PartSize` and the length divided by the part budget, rounded up to 1 MiB. The largest accepted length is the part budget times `MaxPartSize`: 9,744 × 64 MiB = 623,616 MiB (about 609 GiB) at the defaults. Creation rejects a longer upload with a `TusStoreException` (`400`).
- **Deferred length.** The upload uses `PartSize`, so it can grow to the part budget times `PartSize`: 9,744 × 8 MiB = 77,952 MiB (about 76 GiB) at the defaults. Declaring a longer length, or appending past the cap, fails with a `TusStoreException`. Raise `PartSize` to raise the cap; it also raises the memory each PATCH holds.

**Tail segments and memory.** Bytes that do not fill a part are written as a new tail segment object, so a client may send chunks of any size, below 5 MiB included. The store reads the segments back into the next part once enough bytes arrive, and deletes them only after the state write that records that part, so no step drops bytes the store already reported in an offset. Past `MaxTailSegments` segments, the store folds them into one. Each PATCH holds at most `MaxConcurrentPartUploads` + 1 part buffers, each rented at the upload's part size rounded up to a power of two (6 MiB parts take 8 MiB buffers). The buffers come from a pool for sizes up to `MaxPartSize` that keeps up to 8 idle buffers per size for reuse; beyond that, buffers are allocated fresh. The store writes nothing to local disk.

**Disconnects.** When the client disconnects, the store still stores every byte it received: it commits the parts that landed and writes the rest as tail segments, bounded by `PersistTimeout`. Keep `PersistTimeout` below the lease duration of the `IDistributedLock` backend behind `Headless.Tus.DistributedLocks`. Otherwise a lock failover can start a second writer while the first is still persisting; the write fencing then fails one of them.

**Completion.** The final PATCH uploads the last part, marks the upload as completing with one conditional write, completes the multipart upload (or writes the finished object in one request when the upload never filled a part), marks the upload as committing with a second conditional write, writes the completed record, and deletes the state object. The committing write is what keeps the expiry sweep from deleting a finished object: the sweep claims an upload with a conditional write on the same state, so only one of the two lands. If the sweep claimed the upload first, the final PATCH fails with a retryable `500`, records nothing, and deletes the finished object its own write may have created after the sweep deleted that key; a final PATCH that loses to another completion leaves that completion's object in place; once the upload is committing, the sweep leaves it to the client until it is past its expiry and has been committing for over an hour. A zero-length upload writes an empty finished object at creation. If completion is interrupted, HEAD reports the offset from before the final PATCH, the client sends that PATCH again, and the store finishes the completion and tusdotnet raises `OnFileCompleteAsync`. Concatenation builds the final upload inside `CreateFinalFileAsync`, writes its completed record last (if that write fails, it deletes the finished object it built), and keeps the partial uploads.

**Aborted multipart uploads.** By default R2 aborts every incomplete multipart upload 7 days after it was created. With `Provider = R2` and no `MultipartUploadLifetime`, the store applies the same 7 days: once they pass since the upload's first part, the upload reads as missing (`404`) and the cleanup job removes it. An upload whose final PATCH already completed the multipart upload (committing) is exempt: its bytes are in the finished object, so it stays reachable and a re-sent final PATCH records it. A client must therefore finish an R2 upload within 7 days of its first part; set the tus expiration below that. When the provider reports the multipart upload gone (`NoSuchUpload`), the store marks the upload gone and answers with a `TusStoreException`, never a corrupt upload. The store never edits bucket lifecycle rules; if a rule changes R2's window, set `MultipartUploadLifetime` to match. On AWS or an S3-compatible server, an abort-incomplete-multipart-upload lifecycle rule is the operator's choice; when the bucket has one, set `MultipartUploadLifetime` to its window.

**Bucket provisioning.** Creating the bucket is a deploy step. `UseS3` registers a provisioner of kind `S3Bucket` named after `Bucket`: `dotnet <app>.dll resources provision -k S3Bucket` (or `deploy`) creates it, and a bucket the account already owns counts as created. A starting host only verifies it with one `ListObjectsV2` call (`ResourceStartupCheck.GateReadiness`): the host starts, and the `warmup` readiness check stays unhealthy until the bucket exists. On R2, the deploy step needs an API token that can create buckets, while the serving host needs only object read, write, and list rights. `resources purge` deletes the working sets, the completed records, the finished objects those records name, and the handed-off marks, for every tenant and for the uploads kept outside any tenant, and keeps the bucket and every other object. `resources delete` does the same, then deletes the bucket only when nothing else is left in it. A manually constructed store has no provisioner, so create its bucket beforehand.

**Storage requests.** The DI registration lets the store read an upload's state once per tus request, though tusdotnet calls several store methods per request. A store constructed without an `IHttpContextAccessor` reads the state on every call and stays correct.

| tus operation | Storage requests |
|---|---|
| HEAD, incomplete upload | 1 |
| HEAD, completed upload | 2 |
| Creation | 1, plus 1 when expiration is on |
| PATCH that only adds a tail segment | 3 |
| PATCH that writes N parts | N + 4 + ⌈N / 64⌉, plus 1 per tail segment read back, plus 1 on the upload's first part-writing PATCH (creates the multipart upload) |
| Final PATCH, on top of its append | 4 (mark completing, complete, mark committing, write the record), plus 1 delete |
| Expiry sweep | 1 listing per 1,000 in-progress uploads and 1 read per in-progress upload; a removal pass adds 1 multipart-upload listing page and the deletes for each removed upload. Completed uploads cost nothing. |

Under sliding expiration, the first PATCH that writes after creation costs one more state write to refresh the expiry; later PATCHes carry the refresh in their commit. On an upload endpoint with a completion handler, a HEAD for a completed upload also reads the handoff state from storage, up to 3 more requests, and a handler run that succeeds reads the upload once more and writes the handed-off mark.

**Expiry sweep.** `RemoveExpiredFilesAsync` lists the in-progress uploads under `{Prefix}.tus/`, one entry per upload, and reads each state object. It never reads the ambient tenant: it handles the uploads kept outside any tenant first, then, one tenant at a time, the uploads of every tenant that has one in progress. Finding those tenants costs one more listing, sent only when a tenant's working set exists, so the cost follows the tenants with an upload in flight, not every tenant that ever uploaded. Before removing an expired or gone upload it writes the state as gone with `If-Match`, and skips the upload when a request changed it since the read. An upload whose completion already wrote the finished object and marked it committing is removed, with that object, only once it is past the expiry it had before completing and has been committing for over an hour: by then the completion has died with the server, and the client has not re-sent the final PATCH that would finish it. A completion frozen for longer than that hour between two storage calls would record an object the sweep already deleted; the store accepts that risk to reclaim the storage. It also aborts a multipart upload older than one hour that sits on the key of an upload it read but that upload's state does not record, and never touches a multipart upload on any other key. Such a leak comes from a crash, or from a request that created the multipart upload and then lost the state write recording it: the store leaves that empty multipart upload to the sweep rather than aborting it at once, because a 412 can also answer an SDK retry of a write that landed, and the upload is then live. Known limit: MinIO lists multipart uploads only under an exact object key, so on MinIO the sweep finds no leaked multipart uploads. Termination lists the exact key and still aborts them; a bucket abort rule covers the rest.

**Readable store.** `GetFileAsync` returns `null` for an unknown or gone upload. For an incomplete upload it returns the metadata, but `GetContentAsync` throws `InvalidOperationException`, because S3 does not serve the parts of an incomplete multipart upload. Inside `OnFileCompleteAsync` the upload is complete and `GetContentAsync` streams the finished object; a completion handler's `TusCompletedUpload.OpenContentAsync` reads it the same way.

**Object headers.** The finished object's headers are fixed when its multipart upload starts or its single write happens, and S3 cannot change them later without copying the object. `DefaultTusS3ObjectHeadersProvider` stores every object as `application/octet-stream` ([Default content type](#default-content-type)) and sets no other header.

### Install

```bash
dotnet add package Headless.Tus.S3
```

### Setup and use

Register `IAmazonS3` for the server, then select the store inside `AddHeadlessTus`. Cloudflare R2:

```csharp
using Amazon.Runtime;
using Amazon.S3;
using Headless.Tus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
    new BasicAWSCredentials(builder.Configuration["R2:AccessKeyId"], builder.Configuration["R2:SecretAccessKey"]),
    new AmazonS3Config
    {
        ServiceURL = $"https://{builder.Configuration["R2:AccountId"]}.r2.cloudflarestorage.com",
        ForcePathStyle = true,
        AuthenticationRegion = "auto",
        // R2 rejects the CRC checksums the SDK adds by default.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    }));

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseS3(options =>
    {
        options.Bucket = "tus-uploads";
        options.Provider = TusS3Provider.R2;
    });

    tus.AddUploadEndpoint(options =>
    {
        options.MaxSize = 5L * 1024 * 1024 * 1024;
        // Keep it below R2's 7-day multipart upload window. 24 hours is also the default.
        options.Expiration = TimeSpan.FromDays(1);
    });
});

var app = builder.Build();

// No OwnedBy on the endpoint: the signed-in user owns each upload (ICurrentUser).
app.MapHeadlessTus("/files").RequireAuthorization();

app.Run();
```

Amazon S3, with credentials and region from the SDK's default chain (environment, shared profile, instance role):

```csharp
using Amazon.S3;
using Headless.Tus;

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseS3(options =>
    {
        options.Bucket = "tus-uploads";
        // Provider defaults to TusS3Provider.Aws. Match a bucket abort-incomplete-multipart rule, if one is set:
        options.MultipartUploadLifetime = TimeSpan.FromDays(7);
    });

    // Declare the upload endpoints here, as in the R2 example.
});
```

MinIO or another S3-compatible server:

```csharp
using Amazon.Runtime;
using Amazon.S3;
using Headless.Tus;

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
    new BasicAWSCredentials(builder.Configuration["S3:AccessKey"], builder.Configuration["S3:SecretKey"]),
    new AmazonS3Config
    {
        ServiceURL = "http://localhost:9000",
        ForcePathStyle = true,
        AuthenticationRegion = "us-east-1",
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    }));

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseS3(builder.Configuration.GetSection("Tus"));

    // Declare the upload endpoints here, as in the R2 example.
});
```

```json
{
  "Tus": {
    "Bucket": "tus-uploads",
    "Prefix": "uploads/",
    "Provider": "S3Compatible"
  }
}
```

Create the bucket as a deploy step before the host serves uploads:

```bash
dotnet MyApp.dll resources provision -k S3Bucket
```

Custom headers on the finished object:

```csharp
using System.Net.Http.Headers;
using Headless.Tus;

public sealed class DownloadHeadersProvider : ITusS3ObjectHeadersProvider
{
    private readonly DefaultTusS3ObjectHeadersProvider _default = new();

    public async ValueTask<TusS3ObjectHeaders> GetObjectHeadersAsync(Dictionary<string, string> metadata)
    {
        // Content-Type stays application/octet-stream; see Default content type.
        var headers = await _default.GetObjectHeadersAsync(metadata);

        if (!metadata.TryGetValue("filename", out var fileName) || string.IsNullOrWhiteSpace(fileName))
        {
            return headers;
        }

        // The header type encodes the client-supplied name instead of pasting it into the header raw.
        var disposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName };

        return headers with { ContentDisposition = disposition.ToString() };
    }
}

// UseS3 keeps a provider the application registered.
builder.Services.AddSingleton<ITusS3ObjectHeadersProvider, DownloadHeadersProvider>();
```

Metadata values are client input. Validate or encode them before they become stored response headers: allow-list a `filetype` instead of storing it as sent.

### Configuration

`TusS3StoreOptions`:

| Option | Default | Notes |
|---|---|---|
| `Bucket` | `""` | Required. The bucket that holds the uploads. |
| `Prefix` | `"uploads/"` | Key prefix the store owns; must not be empty. A trailing `/` is added when missing. Do not share it with other writers. |
| `Provider` | `TusS3Provider.Aws` | `Aws`, `S3Compatible`, or `R2`; sets the per-request settings and, for `R2`, the default `MultipartUploadLifetime`. |
| `PartSize` | `8 MiB` | Part size for a deferred-length upload, and the minimum for a known one. At least 5 MiB. Sets the deferred-length cap. |
| `MaxPartSize` | `64 MiB` | Largest part size the store picks for a long known-length upload. At least `PartSize`, at most 1 GiB, because each part is buffered in one pooled array. Sets the largest accepted length. |
| `MaxConcurrentPartUploads` | `2` | Parts one PATCH uploads in parallel. At least 1. One PATCH holds up to this + 1 part buffers, each the part size rounded up to a power of two. |
| `PartClaimWindow` | `64` | Part numbers one state write reserves. 1–250. Four windows are held back from the 10,000-part budget, so a wider window lowers both length limits. |
| `MaxTailSegments` | `16` | Tail segments an upload may hold before the store folds them into one. At least 1. |
| `PersistTimeout` | `30 seconds` | How long the store keeps storing received bytes after a client disconnects. Must be positive; keep it below the distributed lock lease. |
| `MultipartUploadLifetime` | `null` (7 days with `Provider = R2`) | When the provider aborts incomplete multipart uploads. Set it to match a bucket abort rule on AWS or an S3-compatible server. Must be positive when set. |

### Runtime behavior

- Construction makes no network call; the bucket must already exist (see **Bucket provisioning**).
- `UseS3` registers `TusS3Store` as a singleton, `IHttpContextAccessor`, `TimeProvider.System` (TryAdd), the default `ITusS3ObjectHeadersProvider` (TryAdd), and the `S3Bucket` provisioner. Options run through FluentValidation with `ValidateOnStart`.
- For cross-node PATCH locking, add `tus.UseDistributedLocks()` from `Headless.Tus.DistributedLocks`.

---

## Headless.Tus.DistributedLocks

Distributed lock-based TUS file lock provider, using `Headless.DistributedLocks` to prevent concurrent PATCH corruption across multiple application instances.

### API and behavior

- `tus.UseDistributedLocks(resourcePrefix?)` on `HeadlessTusSetupBuilder` — registers `DistributedLockTusLockProvider` as the host's `ITusFileLockProvider`; every `MapHeadlessTus` endpoint then uses it without further wiring
- `DistributedLockTusLockProvider` — `ITusFileLockProvider` backed by `IDistributedLock`
- `DistributedLockTusFileLock` — `ITusFileLock` that calls `TryAcquireAsync` with zero wait; returns `false` immediately if another node holds the lock (tusdotnet returns `423 Locked` to the client)
- Lock resource key format: `{resourcePrefix}-{fileId}` with prefix default `tus-file-lock`; give each application its own prefix when several share one `IDistributedLock` backend, so equal file ids cannot contend for the same lock
- Compatible with any `IDistributedLock` backend (Redis, in-memory, etc.)
- Single-flights expired-upload cleanup: one node per interval runs the sweep under the `{resourcePrefix}:expired-uploads-cleanup` lease
- Keeps a completion handler to one node at a time: the handler runs under the upload's file lock, which this provider makes a cross-node lock ([Completion handler](#completion-handler))
- Best-effort mutual exclusion, not fencing: tusdotnet's `ITusFileLock` contract has no hook to observe a lease lost mid-request, so a holder that loses its lease during a backend partition keeps writing until the request ends — the auto-extending lease shrinks that window but cannot eliminate it. The store has to fence: `TusAzureStore` conditions every write on the blob ETag and `TusS3Store` on the state object's ETag, so such a holder's write fails with `412` instead of overwriting the new holder's data. A completion handler has no such fence, which is one more reason it must be safe to repeat

### Install

```bash
dotnet add package Headless.Tus.DistributedLocks
```

### Setup and use

```csharp
using Headless.DistributedLocks;
using Headless.Tus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Register a Headless distributed lock backend (Redis shown; any backend works).
//    The Redis backend resolves the IConnectionMultiplexer from DI when the host starts.
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

// 2. Select the lock provider beside the store.
builder.Services.AddHeadlessTus(tus =>
{
    tus.UseS3(options => options.Bucket = "tus-uploads"); // or UseAzure
    tus.UseDistributedLocks();
    tus.AddUploadEndpoint(options => options.MaxSize = 1024L * 1024 * 1024);
});

var app = builder.Build();

// 3. The mapped endpoint uses the lock provider without further wiring.
app.MapHeadlessTus("/files").RequireAuthorization();

app.Run();
```

An endpoint mapped with tusdotnet's `MapTus` does not pick the provider up: set `FileLockProvider` in its `DefaultTusConfiguration` factory ([Raw MapTus](#raw-maptus)).

### Configuration

`resourcePrefix` (default `tus-file-lock`) is the only setting, beyond registering an `IDistributedLock` provider. The lock acquires with `AcquireTimeout = TimeSpan.Zero` (non-blocking) and `TimeUntilExpires = Timeout.InfiniteTimeSpan` (no expiry while held).

### Runtime behavior

- `UseDistributedLocks()` registers `ITusFileLockProvider` as a singleton (`DistributedLockTusLockProvider`).
- It also registers an internal gate that single-flights the expired-upload cleanup passes across nodes through the same `IDistributedLock`.
- Requires an `IDistributedLock` registration. It is resolved once the host is built, so the order of `AddHeadlessDistributedLocks` and `AddHeadlessTus` does not matter.
