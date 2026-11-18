---
domain: TUS (Resumable Uploads)
packages: Tus, Tus.Azure, Tus.S3, Tus.DistributedLocks
---

# TUS (Resumable Uploads)

> tus 1.0.0 resumable-upload stores for tusdotnet on Azure Blob Storage and on S3-compatible storage (Cloudflare R2, Amazon S3, MinIO), with cross-node file locking.

## Orientation

`tusdotnet` serves the tus protocol. A Headless store decides where the bytes land. All four packages use the `Headless.Tus` namespace.

| Package | Role |
| --- | --- |
| `Headless.Tus` | Shared pieces: `WithTusHeaders()` and `TusCorsDefaults` for browser clients, and `AddTusExpiredUploadsCleanup()`. Every store package brings it in. |
| `Headless.Tus.Azure` | `TusAzureStore`: uploads as Azure block blobs. |
| `Headless.Tus.S3` | `TusS3Store`: uploads as S3 multipart uploads, through the app's `IAmazonS3` client. |
| `Headless.Tus.DistributedLocks` | `AddDistributedLockTusLockProvider()`: one PATCH per file across all nodes, through `IDistributedLock`. |

Add exactly one store package ([Choosing a store](#choosing-a-store)). Add `Headless.Tus.DistributedLocks` when more than one instance serves the endpoint.

A complete backend on Azure. For local development, Azurite serves blobs: `docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0`.

```csharp
using Headless.Tus;
using Microsoft.Extensions.Azure;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

var builder = WebApplication.CreateBuilder(args);

// 1. The storage account ("UseDevelopmentStorage=true" targets Azurite).
builder.Services.AddAzureClients(clients =>
    clients.AddBlobServiceClient(builder.Configuration.GetConnectionString("AzureStorage")));

// 2. The store, with options from the "Tus" section (ContainerName, BlobPrefix, ...), validated at startup.
builder.Services.AddTusAzureStore(builder.Configuration.GetSection("Tus"));

// 3. Remove expired incomplete uploads.
builder.Services.AddTusExpiredUploadsCleanup();

// 4. CORS for browser clients on another origin (tus-js-client, Uppy).
builder.Services.AddCors(options =>
    options.AddPolicy("tus", policy => policy.WithOrigins("https://app.example.com").WithTusHeaders()));

var app = builder.Build();
app.UseCors("tus");

// 5. The tus endpoint.
app.MapTus(
    "/files",
    httpContext => Task.FromResult(new DefaultTusConfiguration
    {
        Store = httpContext.RequestServices.GetRequiredService<TusAzureStore>(),
        UrlPath = "/files",
        AllowedExtensions = TusExtensions.All,
        Expiration = new SlidingExpiration(TimeSpan.FromMinutes(30)),
    }));

app.Run();
```

For S3, replace steps 1 and 2 with an `IAmazonS3` registration and `AddTusS3Store`, and resolve `TusS3Store` in step 5 ([Headless.Tus.S3](#headlesstuss3)).

**Verify:** `dotnet MyApp.dll resources provision -k AzureBlob` reports `Ok`, and `resources verify -k AzureBlob` exits `0` (the `resources` verb needs [`RunHeadlessAsync(args)`](utilities.md#resource-provisioning)). Until the container exists, a started host's readiness endpoint reports the `warmup` check `Unhealthy` naming it; in a test, `await host.Services.WaitForWarmupAsync(ct)` returns once it exists. A `POST /files` with `Tus-Resumable: 1.0.0` and `Upload-Length` returns `201` with a `Location` header.

Any tus 1.0.0 client works against this endpoint (`tus-js-client`, Uppy's `@uppy/tus`, tuspy). [demo/Headless.Tus.Demo](../../demo/Headless.Tus.Demo/README.md) runs this backend with a React frontend that uses Uppy Dashboard and `use-tus`.

## Agent Rules

- Add exactly one store package: `Headless.Tus.Azure` or `Headless.Tus.S3`. `Headless.Tus` alone has no store.
- Call `policy.WithTusHeaders()` on the CORS policy for a browser client on another origin. Without the exposed headers, `tus-js-client` and Uppy cannot read `Location` or `Upload-Offset`, and every upload fails on the first request. Origins and credentials stay your choice.
- Register `services.AddTusExpiredUploadsCleanup()` and set `DefaultTusConfiguration.Expiration`. Nothing removes expired uploads otherwise. The job needs an `ITusExpirationStore`: `AddTusAzureStore` and `AddTusS3Store` register it. For a store you construct yourself, register `services.AddSingleton<ITusExpirationStore>(store)`.
- In a multi-instance deployment, add `Headless.Tus.DistributedLocks`, call `services.AddDistributedLockTusLockProvider()`, and set `FileLockProvider` in the `DefaultTusConfiguration` factory to the `ITusFileLockProvider` from DI. Nothing sets it for you. Without it, two nodes can run PATCHes on one file at once: on Azure that can corrupt the block list, and on S3 all but one PATCH fail with a 5xx the client must retry. A single-node deployment can rely on tusdotnet's in-process lock.
- Register an `IDistributedLock` backend before `AddDistributedLockTusLockProvider()`, for example `services.AddHeadlessDistributedLocks(setup => setup.UseRedis())`.
- Do not map a store's stale-write exception to `400` or another non-retryable 4xx in an exception handler. `Azure.RequestFailedException` with status `412` (Azure) and `AmazonS3Exception` with `412`, `409`, or `404` (S3) reach tusdotnet as unhandled exceptions and answer `500`. tus clients retry a 5xx by sending HEAD and resuming from the committed offset, but treat `400` as fatal. Mapping to `409` keeps the error retryable.
- Create the container or bucket as a deploy step: `dotnet MyApp.dll resources provision` (or `deploy`). Neither store creates it, and neither constructor makes a network call. A development or test host can call `services.AddDeployStepsOnStartup()` instead. A store you construct yourself has no provisioner, so create its container or bucket beforehand.
- Keep the default `application/octet-stream` content type unless you serve uploads from an isolated origin. See [Default content type](#default-content-type).
- Validate file ids from a custom `ITusFileIdProvider`: they become blob names or object keys, and the store rejects unsafe ids with `TusStoreException`.
- Call `AddTusAzureStore` or `AddTusS3Store` once per app. A second call adds its options to the same single store and registers no second store.

## Core Concepts

### Choosing a store

| Store | Use when | Trade-off |
| --- | --- | --- |
| `Headless.Tus.Azure` | Uploads land in Azure Blob Storage, or clients send `Upload-Checksum`. | Supports every tus extension tusdotnet defines, Checksum included. |
| `Headless.Tus.S3` | Uploads land in Cloudflare R2, Amazon S3, or an S3-compatible server. | No Checksum extension. An incomplete upload's content is not readable, only its metadata. On R2 an upload must finish within 7 days of its first part. |

Both stores reject a write from a request whose view is stale, report a completed upload as never expiring, and register their storage as a [provisioned resource](utilities.md#resource-provisioning). To move from one store to the other, swap the client registration and the `AddTus*Store` call. Uploads in flight do not move.

### Concurrent PATCH Safety

tus allows one PATCH at a time per file. Across nodes, `DistributedLockTusLockProvider` (from `Headless.Tus.DistributedLocks`) enforces it: it tries the lock once with no wait, so a second PATCH gets `423 Locked` at once. The lease extends itself while held and expires when its holder crashes, so a file is never stuck.

The lock is best effort. A holder can lose it without noticing (a process pause, a lock-backend failover) while a second PATCH proceeds. Both stores therefore condition their writes on what the request read: the stale PATCH's write fails with a retryable `500` instead of overwriting the other PATCH's data. See **Write fencing** under [Headless.Tus.Azure](#headlesstusazure) and [Headless.Tus.S3](#headlesstuss3).

Do not take an Azure blob lease on an upload blob. The store writes without a lease, so a lease makes its own writes fail with `412`. Coordinate PATCHes through `DistributedLockTusLockProvider`, which locks a separate resource.

### Default content type

`DefaultTusAzureBlobHttpHeadersProvider` and `DefaultTusS3ObjectHeadersProvider` store every upload as `application/octet-stream` and ignore the `filetype` metadata. A browser downloads such an object instead of displaying it.

The client chooses `filetype`. An object served from your storage or CDN origin as `text/html`, `image/svg+xml`, or another type a browser renders or runs, would run the uploader's script in that origin (stored XSS). To store the real type, implement the store's headers provider: allow-list the types you serve instead of storing `filetype` as sent, and serve anything a browser can run from an isolated origin or with `Content-Disposition: attachment`.

---

## Headless.Tus

The CORS helpers and the expired-upload cleanup job shared by every store.

### Setup

```bash
dotnet add package Headless.Tus
```

Every `Headless.Tus.*` store package brings it in.

```csharp
using Headless.Tus;

builder.Services.AddCors(options =>
    options.AddPolicy("tus", policy =>
        policy.WithOrigins("https://app.example.com").WithTusHeaders()));

builder.Services.AddTusExpiredUploadsCleanup(); // needs an ITusExpirationStore
```

**Verify:** a cross-origin `POST /files` from the allowed origin returns `Access-Control-Expose-Headers` that list `Location` and `Upload-Offset`. The cleanup job logs `Removed {Count} expired incomplete upload(s)` at `Information` when a pass removes something, and `Expired-upload cleanup pass failed` at `Error` when a pass throws. The host fails to start when no `ITusExpirationStore` is registered, because the job cannot be resolved.

### Configuration

`TusCorsDefaults` holds the tus CORS surface as lists: `ExposedHeaders`, `AllowedHeaders`, and `AllowedMethods` (with the `PATCH` and `DELETE` that default CORS policies miss). `WithTusHeaders()` applies all three.

| `TusExpiredUploadsCleanupOptions` | Default | Notes |
| --- | --- | --- |
| `Interval` | 5 minutes | Time between passes. Each pass scans the store, so choose a longer interval for a large container or bucket. Must be positive. |

### Design and runtime behavior

- **Incomplete uploads only.** A Headless store never reports a completed upload as expired, so the job cannot delete finished data.
- **Schedule.** The first pass runs when the host starts, to reclaim uploads that expired while it was down. Then one pass runs per `Interval`. A failed pass is logged and the next tick runs normally.
- **Several nodes.** With `AddDistributedLockTusLockProvider()` registered, a node runs a pass only when it takes the `{resourcePrefix}:expired-uploads-cleanup` lease, held for 90% of the interval, so the cluster sweeps about once per interval. A crashed node delays cleanup by at most one interval. Without the lock provider, every node sweeps on every tick: deletes are idempotent, but the scan load multiplies and each node logs its own counts.
- **Races with a resuming client.** The job holds no tus file lock. Each store deletes an expired upload only when it is unchanged since the pass found it, so a resume that committed data or refreshed a sliding expiration in between skips the delete. A resume that has started but written nothing yet can still lose its upload. Register `Headless.Tus.DistributedLocks` to close that window.

---

## Headless.Tus.Azure

`TusAzureStore` stores each upload as an Azure block blob. It supports Creation, Creation Defer Length, Concatenation, Expiration, Checksum, Termination, and reading a file back (`GetFileAsync`).

### Setup

```bash
dotnet add package Headless.Tus.Azure
```

Register the store through DI ([Orientation](#orientation)), or construct it yourself:

```csharp
using Azure.Storage.Blobs;
using Headless.Tus;
using tusdotnet;
using tusdotnet.Models;

var builder = WebApplication.CreateBuilder(args);

var blobServiceClient = new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]);

// A store you construct has no provisioner: create the "uploads" container in the deployment.
var tusStore = new TusAzureStore(
    blobServiceClient,
    new TusAzureStoreOptions
    {
        ContainerName = "uploads",
        BlobPrefix = "tus/",
    }
);

var app = builder.Build();

app.MapTus("/files", async _ => new DefaultTusConfiguration { Store = tusStore, UrlPath = "/files" });

app.Run();
```

`AddTusAzureStore` takes an `IConfiguration` section, an `Action<TusAzureStoreOptions>`, or an `Action<TusAzureStoreOptions, IServiceProvider>`. It needs a `BlobServiceClient` in DI, and it uses an `ITusAzureBlobHttpHeadersProvider` or `ITusFileIdProvider` registered before the call. It registers `TusAzureStore` and `ITusExpirationStore` as singletons, adds `IHttpContextAccessor`, and registers the `AzureBlob` provisioner named after `ContainerName`. The constructor takes the same optional services: `new TusAzureStore(blobServiceClient, options, blobHttpHeadersProvider, fileIdProvider, loggerFactory, timeProvider, httpContextAccessor)`.

**Verify:** `dotnet MyApp.dll resources verify -k AzureBlob` exits `0`. Invalid `TusAzureStoreOptions` fail the host start with `OptionsValidationException`.

Custom HTTP headers per upload:

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

var store = new TusAzureStore(
    blobServiceClient,
    options,
    blobHttpHeadersProvider: new MyHeadersProvider()
);
```

With DI, register the provider before `AddTusAzureStore`: `services.AddSingleton<ITusAzureBlobHttpHeadersProvider, MyHeadersProvider>()`. The headers apply at creation and on every later commit, so a custom content type survives each PATCH.

### Configuration

| `TusAzureStoreOptions` | Default | Notes |
| --- | --- | --- |
| `ContainerName` | `"tus-uploads"` | Azure Blob container. |
| `BlobPrefix` | `"uploads/"` | Prefix of every blob name the store writes. |
| `ContainerPublicAccessType` | `PublicAccessType.None` | Access type `resources provision` applies when it creates the container. Ignored for an existing container. |
| `EnableChunkSplitting` | `true` | Splits a PATCH body into several Azure blocks. |
| `BlobDefaultChunkSize` | 4 MB | Block size for medium uploads. 1 byte to 100 MB, and at most `BlobMaxChunkSize`. |
| `BlobMaxChunkSize` | 16 MB | Block size for uploads of 100 MB and more, and the per-request buffer for them. At most 100 MB, a memory cap the store sets (Azure allows 4,000 MiB per block). |
| `MaxNoSplitBufferSize` | 256 MB | With `EnableChunkSplitting = false`, the largest PATCH body the store buffers. 1 byte to about 2 GB. Ignored when splitting is on. |
| `DeletePartialFilesOnConcat` | `false` | Deletes the partial uploads after a final upload commits, best effort. Keep `false` when clients reuse partials. |

Block size by declared upload length: under 10 MB, `min(BlobDefaultChunkSize, length)`; 10 MB to 100 MB, `BlobDefaultChunkSize`; 100 MB and more, `BlobMaxChunkSize`.

### Design and runtime behavior

**Upload limits.**

- An upload holds at most 50,000 committed blocks, Azure's limit. A block size too small for a very large upload throws `TusStoreException` at commit. Raise `BlobDefaultChunkSize` and `BlobMaxChunkSize` for fewer, larger blocks.
- With `EnableChunkSplitting = false`, the whole PATCH body is buffered and staged as one block. `MaxNoSplitBufferSize` is then the only memory bound for a deferred-length upload, because its length is unknown and tusdotnet lifts Kestrel's body limit for it. A larger body fails with `TusStoreException` (HTTP `400`). For a declared length, tusdotnet answers an over-length PATCH with `413` before the store buffers anything.
- The client's `Upload-Metadata` is stored verbatim and returned byte for byte on HEAD. Keys keep their casing, and values may decode to any UTF-8 content. A value that is not printable ASCII, or longer than about 7 KB (Azure allows 8 KB of blob metadata), fails with `TusStoreException`. A concatenation final also stores its partial ids, and `CreateFinalFileAsync` throws `TusStoreException` when ids plus metadata would pass 8 KB.
- A file id must not contain `/`, `\`, `..`, `,`, or a control character, or start or end with whitespace. Every entry point rejects such an id with `TusStoreException`, in addition to the id provider's own `ValidateId`.

**Offsets and checksums.** The offset is the blob's committed length, so staged blocks never advance it. SHA1, SHA256, SHA512, and MD5 are checked for both `Upload-Checksum` forms, with a constant-time comparison.

- Header form: blocks are staged and committed only after the checksum passes. On a mismatch they stay uncommitted, and Azure removes them within 7 days.
- Trailer form (`checksum-trailer`): data commits during the append. On a mismatch, or a missing or faulty trailer, the store rolls the chunk back.

**Disconnects and cancellation.** Reading the request body follows the request token, which is the disconnect signal. Everything that stores bytes already received runs to completion: a disconnected PATCH commits what arrived, and the sliding-expiration refresh and checksum verification run even after tusdotnet cancels the request token. A checksum PATCH whose chunk fully arrived and verified just before the disconnect is committed, and the resuming client sees the advanced offset on HEAD. Expired-file deletion finishes once a file is enumerated, and a cancelled enumeration returns the files found so far.

**Expiration.** `GetExpirationAsync` returns `null` for a completed upload, so a completed upload stays reachable through HEAD and DELETE after its sliding window passes.

**Write fencing.** Every block-list commit and metadata write is conditioned on the blob ETag the request read. A request whose view is stale (it lost its file lock and another PATCH committed meanwhile) fails with `Azure.RequestFailedException` status `412`, which tusdotnet answers with `500` and the client retries. The sliding-expiration refresh is the exception: it re-reads and retries up to three times instead of failing a PATCH whose data already committed. A concatenated final blob is written only when no blob has that name yet. A tus `DELETE` and the deletion of partials after concatenation are unconditional. The cleanup job's deletes are conditional ([Headless.Tus](#headlesstus)).

**Storage requests.** With the `IHttpContextAccessor` that `AddTusAzureStore` adds, the store reads a blob once per tus request: a HEAD costs one request, a PATCH five, a checksum-header PATCH six. A store constructed without an accessor reads the blob on every store call and stays correct.

**Concatenation.** The final upload copies blocks server-side. When the server cannot copy (Azurite answers `501`, and a private container answers `403` because the source has no SAS), the store downloads and re-uploads the data instead.

**Provisioning.** `resources provision` creates the container with `ContainerPublicAccessType`, so the serving host needs no container-create permission. A starting host checks that the container exists with one request and holds the `warmup` readiness check until it does. Change that per kind with `ResourceProvisioningOptions.StartupChecks`. `resources purge` deletes the blobs under `BlobPrefix` and keeps the container. `resources delete` also deletes the container, but only when nothing else is left in it.

---

## Headless.Tus.S3

`TusS3Store` stores each upload as one S3 multipart upload, in Cloudflare R2, Amazon S3, or an S3-compatible server such as MinIO. It supports Creation (with upload), Creation Defer Length up to a cap, Concatenation, Expiration, Termination, and reading a file back. It does not support Checksum, so tusdotnet does not advertise it. Use `Headless.Tus.Azure` when clients send `Upload-Checksum`.

### Setup

```bash
dotnet add package Headless.Tus.S3
```

Register `IAmazonS3` for your server, then the store. Cloudflare R2:

```csharp
using Amazon.Runtime;
using Amazon.S3;
using Headless.Tus;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

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

builder.Services.AddTusS3Store(options =>
{
    options.Bucket = "tus-uploads";
    options.Provider = TusS3Provider.R2;
});

builder.Services.AddTusExpiredUploadsCleanup();

var app = builder.Build();

app.MapTus(
    "/files",
    httpContext => Task.FromResult(new DefaultTusConfiguration
    {
        Store = httpContext.RequestServices.GetRequiredService<TusS3Store>(),
        UrlPath = "/files",
        AllowedExtensions = TusExtensions.All,
        // Below R2's 7-day multipart upload window.
        Expiration = new SlidingExpiration(TimeSpan.FromDays(1)),
    }));

app.Run();
```

Amazon S3, with credentials and region from the SDK's default chain (environment, shared profile, instance role):

```csharp
using Amazon.S3;
using Headless.Tus;

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());

builder.Services.AddTusS3Store(options =>
{
    options.Bucket = "tus-uploads";
    // Provider defaults to TusS3Provider.Aws. Match a bucket abort-incomplete-multipart rule, if one is set:
    options.MultipartUploadLifetime = TimeSpan.FromDays(7);
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

builder.Services.AddTusS3Store(builder.Configuration.GetSection("Tus"));
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

`AddTusS3Store` takes an `IConfiguration` section, an `Action<TusS3StoreOptions>`, or an `Action<TusS3StoreOptions, IServiceProvider>`. It resolves `IAmazonS3` from DI and does not configure it. It uses an `ITusS3ObjectHeadersProvider` or `ITusFileIdProvider` registered before the call. It registers `TusS3Store` and `ITusExpirationStore` as singletons, adds `IHttpContextAccessor`, and registers the `S3Bucket` provisioner named after `Bucket`. To construct the store yourself, call `new TusS3Store(s3, options, objectHeadersProvider, fileIdProvider, loggerFactory, timeProvider, httpContextAccessor)`. Every parameter after `options` is optional, and invalid options throw `ValidationException`.

**Verify:** `dotnet MyApp.dll resources provision -k S3Bucket` reports `Ok`, and `resources verify -k S3Bucket` exits `0`. Until the bucket exists, a started host's readiness endpoint reports the `warmup` check `Unhealthy` naming it. Invalid `TusS3StoreOptions` fail the host start with `OptionsValidationException`.

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

// Register before AddTusS3Store, which keeps an existing registration.
builder.Services.AddSingleton<ITusS3ObjectHeadersProvider, DownloadHeadersProvider>();
```

`TusS3ObjectHeaders` carries `ContentType`, `CacheControl`, `ContentDisposition`, `ContentEncoding`, and `ContentLanguage`. They are fixed when the upload's multipart upload starts, and S3 cannot change them later without copying the object. Metadata values are client input: validate or encode them before they become stored response headers.

### Configuration

| `TusS3StoreOptions` | Default | Notes |
| --- | --- | --- |
| `Bucket` | `""` | Required. The bucket that holds the uploads. |
| `Prefix` | `"uploads/"` | The key prefix the store owns. Must not be empty. A missing trailing `/` is added. Share it with no other writer, because `resources purge` deletes objects there. |
| `Provider` | `TusS3Provider.Aws` | `Aws`, `S3Compatible`, or `R2`. Sets per-request settings and, for `R2`, the default `MultipartUploadLifetime`. |
| `PartSize` | 8 MiB | Part size for a deferred-length upload, and the minimum for a known length. At least 5 MiB. Sets the deferred-length cap. |
| `MaxPartSize` | 64 MiB | Largest part size the store picks for a long known-length upload. At least `PartSize`, at most 1 GiB. Sets the largest accepted length. |
| `MaxConcurrentPartUploads` | `2` | Parts one PATCH uploads in parallel. At least 1. |
| `PartClaimWindow` | `64` | Part numbers one state write reserves. 1 to 250. A wider window lowers both length limits. |
| `MaxTailSegments` | `16` | Tail segments an upload holds before the store merges them into one. At least 1. |
| `PersistTimeout` | 30 seconds | How long the store keeps storing received bytes after a client disconnects. Must be positive. Keep it below the distributed lock lease. |
| `MultipartUploadLifetime` | `null` (7 days with `Provider = R2`) | When the server aborts an incomplete multipart upload. Set it to match a bucket abort rule on AWS or an S3-compatible server. Must be positive when set. |

### Design and runtime behavior

**S3 client settings.** The app registers `IAmazonS3`, and registration does not check its settings:

| Server | `ServiceURL` | `ForcePathStyle` | `AuthenticationRegion` | Checksum settings | `Provider` |
| --- | --- | --- | --- | --- | --- |
| Cloudflare R2 | `https://<account-id>.r2.cloudflarestorage.com` | `true` | `auto` | `WHEN_REQUIRED` (required) | `R2` |
| Amazon S3 | SDK default | SDK default | SDK default | SDK default | `Aws` |
| MinIO or another S3-compatible server | The server URL; `http` allowed | `true` | The server's region, often `us-east-1` | `WHEN_REQUIRED` (most servers reject the SDK's default CRC checksums) | `S3Compatible` |

`Provider` changes only per-request settings. Every write sends a plain `Content-Length` body. `R2` sends unsigned payloads, which skips a hash pass over every part. `S3Compatible` keeps payloads signed, because the SDK refuses unsigned payloads over plain `http`. `Aws` keeps the SDK default.

Set the client's `Timeout` and `MaxErrorRetry` deliberately. Completion, the expiry refresh after a PATCH, and the cleanup removals ignore cancellation so a disconnect or shutdown cannot interrupt them halfway. The client's timeout and retry count are then the only bound on a call to a slow or unreachable server.

R2 is tested against a real R2 bucket. Amazon S3 is tested against LocalStack, not a real AWS account. MinIO is supported as an S3-compatible server; its upstream repository was archived in April 2026.

**Object layout.** For an upload with file id `{id}`:

- `{Prefix}{id}`: the finished object, written when the upload completes.
- `{Prefix}.tus/{id}/info`: the JSON state of an incomplete upload: length, part size, metadata, expiry, and committed parts.
- `{Prefix}.tus/{id}/seg/...`: tail segments, bytes received that do not yet fill a part.
- `{Prefix}.tus-completed/{id}`: the record a completed upload keeps beside its finished object.

The tus metadata lives in the state object, so S3's 2 KB user-metadata limit does not apply. The store rejects a file id that starts with `.`, contains `/`, `\`, or a control character, or starts or ends with whitespace, with `TusStoreException`.

**Length limits.** S3 and R2 allow 10,000 parts per multipart upload, and R2 needs every part except the last to be the same size. The store fixes the part size at creation and holds four claim windows in reserve, so an upload spans at most `10,000 − 4 × PartClaimWindow` parts: 9,744 at the defaults.

- **Known length.** The part size is the larger of `PartSize` and the length divided by the part budget, rounded up to 1 MiB. The largest accepted length is the part budget × `MaxPartSize`: 623,616 MiB (about 609 GiB) at the defaults. Creation rejects a longer upload with `TusStoreException` (`400`).
- **Deferred length.** The upload uses `PartSize`, so it can grow to the part budget × `PartSize`: 77,952 MiB (about 76 GiB) at the defaults. Declaring a longer length, or appending past the cap, fails with `TusStoreException`. Raise `PartSize` to raise the cap; that also raises the memory each PATCH holds.

**Memory.** A client may send chunks of any size, below 5 MiB included: bytes that do not fill a part are stored as tail segments and read back into the next part. One PATCH holds at most `MaxConcurrentPartUploads` + 1 part buffers, each the part size rounded up to a power of two (6 MiB parts take 8 MiB buffers). The store writes nothing to local disk.

**Disconnects.** When the client disconnects, the store still stores every byte it received, bounded by `PersistTimeout`. Keep `PersistTimeout` below the lease of the `IDistributedLock` backend. Otherwise a lock failover can start a second writer while the first is still storing, and the write fencing fails one of them.

**Write fencing.** Creation writes the state object only when it does not exist, and every later write is conditioned on the ETag the request read. A request whose view is stale fails with `AmazonS3Exception` (`412 PreconditionFailed`, `409 ConditionalRequestConflict`, or `404 NoSuchKey`), which tusdotnet answers with `500` and the client retries from HEAD. A PATCH reserves its part numbers before it uploads any part, so a stale writer fails before it can replace a part on R2.

**Completion.**

- The final PATCH completes the multipart upload, writes the completed record, and deletes the state object. A zero-length upload writes an empty finished object at creation.
- If completion is interrupted, HEAD reports the offset from before the final PATCH. The client sends that PATCH again, the store finishes the completion, and tusdotnet raises `OnFileCompleteAsync`.
- If the cleanup job claimed the upload first, the final PATCH fails with a retryable `500` and leaves no finished object.
- Concatenation builds the final upload inside `CreateFinalFileAsync`, copies parts server-side, accepts partials of any size, and keeps the partial uploads.

**Aborted multipart uploads.** By default R2 aborts an incomplete multipart upload 7 days after it was created. With `Provider = R2` and no `MultipartUploadLifetime`, the store applies the same 7 days: once they pass since the upload's first part, the upload reads as missing (`404`) and the cleanup job removes it. An upload whose final PATCH already completed the multipart upload stays reachable. Finish an R2 upload within 7 days of its first part, and set the tus expiration below that. When the server reports the multipart upload gone (`NoSuchUpload`), the store answers with `TusStoreException`, never a corrupt upload. The store never edits bucket lifecycle rules. If a rule changes R2's window, or a bucket on AWS or another server has an abort rule, set `MultipartUploadLifetime` to match.

**Provisioning.** `resources provision -k S3Bucket` creates the bucket, and a bucket the account already owns counts as created. On R2, that deploy step needs a token that can create buckets, while the serving host needs only object read, write, and list rights. A starting host checks the bucket with one `ListObjectsV2` call and holds the `warmup` readiness check until it exists. `resources purge` deletes the store's objects under `Prefix` (in-progress uploads, completed records, and the finished objects they name) and keeps the bucket and every other object. `resources delete` does the same, then deletes the bucket only when nothing else is left in it.

**Storage requests.** With the `IHttpContextAccessor` that `AddTusS3Store` adds, the store reads an upload's state once per tus request. A store without an accessor reads the state on every store call and stays correct. Request counts, which R2 and S3 bill per operation:

| tus operation | Storage requests |
| --- | --- |
| HEAD, incomplete upload | 1 |
| HEAD, completed upload | 2 |
| Creation | 1, plus 1 when expiration is on |
| PATCH that only adds a tail segment | 3 |
| PATCH that writes N parts | N + 4 + ⌈N / 64⌉, plus 1 per tail segment read back, plus 1 on the first part-writing PATCH |
| Final PATCH, on top of its append | 4, plus 1 delete |
| Cleanup pass | 1 listing per 1,000 in-progress uploads and 1 read per in-progress upload. A pass that removes uploads adds 1 multipart-upload listing page and the deletes. Completed uploads cost nothing. |

Under sliding expiration, the first PATCH that writes after creation costs one more state write to refresh the expiry.

**Cleanup.** `RemoveExpiredFilesAsync` lists the in-progress uploads and reads each state object. It skips an upload a request changed since the read. An upload stuck mid-completion is removed, with its finished object, only once it is past its earlier expiry and has been completing for over an hour. The pass also aborts a leaked multipart upload, older than one hour, on the key of an upload it read. On MinIO, which lists multipart uploads only by exact key, the pass finds no leaked multipart uploads; a tus `DELETE` still aborts them, and a bucket abort rule covers the rest.

**Reading uploads.** `GetFileAsync` returns `null` for an unknown or removed upload. For an incomplete upload it returns the metadata, but `GetContentAsync` throws `InvalidOperationException`, because S3 does not serve the parts of an incomplete multipart upload. Inside `OnFileCompleteAsync` the upload is complete, and `GetContentAsync` streams the finished object.

---

## Headless.Tus.DistributedLocks

`DistributedLockTusLockProvider`, an `ITusFileLockProvider` over `IDistributedLock`. It keeps one PATCH per file across nodes and runs the cleanup job on one node per interval.

### Setup

```bash
dotnet add package Headless.Tus.DistributedLocks
```

```csharp
using Headless.Tus;
using StackExchange.Redis;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. A Headless distributed lock backend (Redis shown; any backend works).
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

// 2. The tus lock provider.
builder.Services.AddDistributedLockTusLockProvider();

var app = builder.Build();

// 3. Wire the lock provider into the tus configuration.
app.MapTus(
    "/files",
    async ctx =>
    {
        var lockProvider = ctx.RequestServices.GetRequiredService<ITusFileLockProvider>();

        return new DefaultTusConfiguration
        {
            Store = tusStore, // your TusAzureStore or TusS3Store
            UrlPath = "/files",
            FileLockProvider = lockProvider,
        };
    }
);

app.Run();
```

**Verify:** start two PATCH requests for the same file at once. The second gets `423 Locked`. Resolving `ITusFileLockProvider` throws when no `IDistributedLock` is registered.

### Configuration

`AddDistributedLockTusLockProvider(string resourcePrefix = "tus-file-lock")` is the only setting. The lock resource for a file is `{resourcePrefix}-{fileId}`. When several tus endpoints share one `IDistributedLock` backend, give each its own prefix so equal file ids from different stores do not contend.

### Design and runtime behavior

- `ITusFileLockProvider` is a singleton.
- A lock attempt does not wait. When another node holds the file, tusdotnet answers `423 Locked`.
- The lease has the lock backend's default lifetime and extends itself while held. A crashed holder's lease expires, so the file is freed.
- The lock gives mutual exclusion, not fencing. A holder that loses its lease during a backend partition keeps writing until its request ends, and the store's write fencing then fails its write with a retryable `500`.
- With `AddTusExpiredUploadsCleanup()` also registered, the cleanup job runs on one node per interval ([Headless.Tus](#headlesstus)).
