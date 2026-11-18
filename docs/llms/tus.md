---
domain: TUS (Resumable Uploads)
packages: Tus, Tus.Azure, Tus.S3, Tus.DistributedLocks
---

# TUS (Resumable Uploads)

> tus 1.0.0 upload endpoints over tusdotnet that bind each upload to its owner and tenant, validate it, and hand the application the finished file, with stores on Azure Blob Storage and S3-compatible storage (Cloudflare R2, Amazon S3, MinIO) and cross-node file locking.

## Orientation

tusdotnet serves the tus protocol. A store decides where the bytes land. An upload endpoint decides who may reach an upload and what may be uploaded. All four packages use the `Headless.Tus` namespace.

| Package | Role |
| --- | --- |
| `Headless.Tus` | `AddHeadlessTus` (store selection, upload endpoints, expired-upload cleanup), `MapHeadlessTus`, `ITusUploadValidator`, `ITusUploads`, `tenancy.Tus(...)`, and `WithTusHeaders()`. Every store package brings it in. |
| `Headless.Tus.Azure` | `UseAzure`: `TusAzureStore`, uploads as Azure block blobs. |
| `Headless.Tus.S3` | `UseS3`: `TusS3Store`, uploads as S3 multipart uploads through the app's `IAmazonS3` client. |
| `Headless.Tus.DistributedLocks` | `UseDistributedLocks`: one request per upload across all nodes, through `IDistributedLock`. |

The default path:

1. Call `services.AddHeadlessTus(tus => ...)` once and select exactly one store in it: `UseAzure`, `UseS3`, or `UseStore` for any other `ITusStore` ([Choosing a store](#choosing-a-store)).
2. Declare each upload endpoint in the same call with `tus.AddUploadEndpoint(...)`, and map it with `app.MapHeadlessTus(pattern)`.
3. Add `tus.UseDistributedLocks()` when more than one node serves uploads.

`MapHeadlessTus` binds every upload to the endpoint, owner, and tenant that created it ([Isolation](#isolation)), checks a creating request's size, type, metadata, and first bytes ([Validation](#validation)), and lets the application take the finished upload through `ITusUploads` ([Finished uploads](#finished-uploads)).

A complete backend on Azure. For local development, Azurite serves blobs: `docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0`.

```csharp
using System.Security.Claims;
using Headless.Tus;
using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

// 1. The storage account ("UseDevelopmentStorage=true" targets Azurite).
builder.Services.AddAzureClients(clients =>
    clients.AddBlobServiceClient(builder.Configuration.GetConnectionString("AzureStorage")));

// 2. The store, with options from the "Tus" section (ContainerName, BlobPrefix, ...), and one upload
//    endpoint. Both are validated at startup. The expired-upload cleanup runs by default.
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

// 3. CORS for browser clients on another origin (tus-js-client, Uppy).
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

For S3, replace step 1 with an `IAmazonS3` registration and `UseAzure` with `UseS3` ([Headless.Tus.S3](#headlesstuss3)).

**Verify:** `dotnet MyApp.dll resources provision -k AzureBlob` reports `Ok`, and `resources verify -k AzureBlob` exits `0` (the `resources` verb needs [`RunHeadlessAsync(args)`](utilities.md#resource-provisioning)). Until the container exists, a started host's readiness endpoint reports the `warmup` check `Unhealthy` naming it. In a test, `await host.Services.WaitForWarmupAsync(ct)` returns once it exists. An authenticated `POST /files` with `Tus-Resumable: 1.0.0`, `Upload-Length`, and `Upload-Metadata` carrying `filename` and `filetype` (`video/mp4`) returns `201` with a `Location` header. With no signed-in user it returns `401`.

Any tus 1.0.0 client works (`tus-js-client`, Uppy's `@uppy/tus`, tuspy). [demo/Headless.Tus.Demo](../../demo/Headless.Tus.Demo/README.md) runs an Azure-backed host with a React frontend that uses Uppy Dashboard and `use-tus`. It maps its endpoint with tusdotnet's `MapTus`, so it shows the protocol and the store, not the upload endpoint layer.

## Agent Rules

- Register TUS once with `services.AddHeadlessTus(tus => ...)` and select exactly one store in it: `UseAzure`, `UseS3`, or `UseStore`. `Headless.Tus` ships no store. No store, two stores, or a second `AddHeadlessTus` call throws `InvalidOperationException`.
- Map uploads with `app.MapHeadlessTus(pattern)`, or `MapHeadlessTus(pattern, name)`, not with tusdotnet's `MapTus`. Only `MapHeadlessTus` applies isolation, validation, the delete rule, the lock provider, and the request body limit ([Raw MapTus](#raw-maptus)).
- Give every upload endpoint an owner: `OwnedBy(...)`, or `ICurrentUser.UserId` when it has none (`builder.AddHeadless()` registers `ICurrentUser`). The layer trusts the key. When it comes from a route value, such as a workspace id, put an authorization policy on the mapped endpoint that proves the caller may act for it ([Isolation](#isolation)).
- Set `MaxSize` on every upload endpoint. Startup fails when it is not positive. A reverse proxy or IIS in front of the host keeps its own body limit ([Request body limits](#request-body-limits)).
- Take a finished upload with `ITusUploads.FindAsync(fileId)`, or `FindAsync(fileId, endpointName)`, inside the request that received the id, not through the store. Pass `FindUncheckedAsync(fileId)` only an id the application already checked ([Finished uploads](#finished-uploads)).
- Delete a finished upload with `TusUpload.DeleteAsync` when you are done with it. It never expires, its client cannot delete it (`403`), and the layer removes none.
- Make the step that records or copies a finished upload safe to repeat, for example keyed on `FileId`. `FindAsync` reserves nothing.
- Do not rely on the first-bytes check for Office files: an allowed docx, xlsx, or pptx accepts any ZIP archive, and an allowed doc, xls, or ppt accepts any compound file ([Validation](#validation)).
- In a tenant-aware host, add `tenancy.Tus(t => t.ScopeByTenant())`. It makes a tenant mandatory and keeps each tenant's uploads under `.tenants/{tenant}/` in the `UseS3` and `UseAzure` stores ([Tenant scoping](#tenant-scoping)).
- Call `policy.WithTusHeaders()` on the CORS policy for a browser client on another origin. Without the exposed headers, `tus-js-client` and Uppy cannot read `Location` or `Upload-Offset`, and every upload fails on the first request. Add `.AllowCredentials()` when the client signs in with a cookie.
- In a multi-node deployment, add `Headless.Tus.DistributedLocks`, call `tus.UseDistributedLocks()`, and register an `IDistributedLock` backend, for example `services.AddHeadlessDistributedLocks(setup => setup.UseRedis())`. Without it, two nodes can write one upload at once: on Azure that can corrupt the block list, and on S3 all but one write fail with a `5xx` the client must retry.
- Do not map a store's stale-write exception to `400` or another non-retryable 4xx. `Azure.RequestFailedException` with status `412` (Azure) and `AmazonS3Exception` with `412`, `409`, or `404` (S3) answer `500`, which tus clients retry from HEAD. They treat `400` as fatal. Map to `409` if you map at all.
- Create the container or bucket as a deploy step: `dotnet MyApp.dll resources provision` (or `deploy`). Neither store creates it, and neither constructor makes a network call. A development or test host can call `services.AddDeployStepsOnStartup()` instead. A store you construct yourself has no provisioner.
- Keep the default `application/octet-stream` content type unless you serve uploads from an isolated origin ([Default content type](#default-content-type)).
- Validate file ids from a custom `ITusFileIdProvider`: they become blob names or object keys, and the store rejects unsafe ids with `TusStoreException`.

## Core Concepts

### Choosing a store

| Store | Use when | Trade-off |
| --- | --- | --- |
| `Headless.Tus.Azure` | Uploads land in Azure Blob Storage, or clients send `Upload-Checksum`. | Supports every tus extension tusdotnet defines, Checksum included. |
| `Headless.Tus.S3` | Uploads land in Cloudflare R2, Amazon S3, or an S3-compatible server. | No Checksum. An incomplete upload's content is not readable. On R2 an upload must finish within 7 days of its first part. |
| `UseStore(...)` | Any other `ITusStore`, such as tusdotnet's `TusDiskStore` for one node or local development: `tus.UseStore(new TusDiskStore("/var/lib/app/uploads"))`. | No per-tenant layout and no provisioner. Must honor the [store contract](#stores-passed-to-usestore). |

Both Headless stores reject writes from a stale request, report a completed upload as never expiring, and register their storage as a [provisioned resource](utilities.md#resource-provisioning). To switch, swap the client registration and the `Use*` call. Uploads in flight do not move. An upload endpoint offers the store's tus extensions except Concatenation, so what the store sections say about concatenation applies to the store itself and to a raw `MapTus` endpoint.

### Stores passed to UseStore

`ITusUploads` needs `ITusReadableStore` on the store to open content and `ITusTerminationStore` to delete. A store of your own must also:

- **Answer existence through metadata.** `GetUploadMetadataAsync` returns `null`, or throws `TusStoreException`, for an id the store never issued. Any other exception counts as a store failure (`503`, or `423` to a status request), so the upload reads as unavailable instead of not found.
- **Implement `ITusPipelineStore` if it hashes as it reads.** The endpoint reads the first bytes from the request pipe and hands a stream-only store the pipe wrapped as a stream, for which tusdotnet's `GetUploadChecksumInfo()` returns `null`.
- **Expect cleanup unsynchronized with writes.** The cleanup calls `RemoveExpiredFilesAsync` with no file lock. tusdotnet's `TusDiskStore` lists expired uploads, then deletes the list, so a write that finishes an upload in between loses it. The Headless stores delete an upload only when it is unchanged since the pass found it.

### Concurrent PATCH Safety

tus allows one PATCH at a time per upload. tusdotnet's in-process lock enforces it on one node. `tus.UseDistributedLocks()` enforces it across nodes: the lock is tried once with no wait, so a second request gets `423 Locked` at once, and a crashed holder's lease expires. Every `MapHeadlessTus` endpoint uses it, and so does `ITusUploads`, which decides that an upload is finished while holding the lock.

The lock is best effort. A holder can lose it without noticing (a process pause, a lock-backend failover). Both stores therefore condition their writes on what the request read, so the stale request fails with a retryable `500` instead of overwriting the other request's data. See **Write fencing** under [Headless.Tus.Azure](#headlesstusazure) and [Headless.Tus.S3](#headlesstuss3).

Do not take an Azure blob lease on an upload blob. The store writes without a lease, so a lease makes its own writes fail with `412`.

### Default content type

`DefaultTusAzureBlobHttpHeadersProvider` and `DefaultTusS3ObjectHeadersProvider` store every upload as `application/octet-stream` and ignore the `filetype` metadata. A browser downloads such an object instead of displaying it.

The client chooses `filetype`. An object served from your storage or CDN origin as `text/html`, `image/svg+xml`, or another type a browser renders or runs would run the uploader's script in that origin (stored XSS). To store the real type, implement the store's headers provider: allow-list the types you serve, and serve anything a browser can run from an isolated origin or with `Content-Disposition: attachment`.

---

## Headless.Tus

The `AddHeadlessTus` registration, upload endpoints, finished-upload lookup, the tenancy seam, the CORS helpers, and the expired-upload cleanup.

### Setup

```bash
dotnet add package Headless.Tus
```

Every store package brings it in. Two named endpoints over one S3 store, one owned by a workspace and one by the signed-in user:

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
        .ValidateWith<VideoQuotaValidator>();

    // No OwnedBy: the signed-in user owns the upload (ICurrentUser).
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

A validator needs no registration of its own:

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
```

Taking a finished upload. The client finishes the upload, then sends its file id in a request of the application's own:

```csharp
using Headless.Tus;

public sealed record CreateLesson(string Title, string UploadId);

public interface ILessonCatalog
{
    Task AddAsync(string title, string uploadId, long length, CancellationToken cancellationToken);
}

// The same route value and policy as the upload endpoint, so the owner resolved here is the upload's.
app.MapPost(
        "/workspaces/{workspaceId}/lessons",
        async (
            CreateLesson command,
            ITusUploads uploads,
            ILessonCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            var upload = await uploads.FindAsync(command.UploadId, "videos", cancellationToken);

            if (upload is null)
            {
                // Unknown, another owner's, another tenant's, another endpoint's, or not finished.
                return Results.NotFound();
            }

            // Safe to repeat: the catalog keys its record on the upload id. The upload stays in the
            // store as the video's home, and its client can no longer delete it.
            await catalog.AddAsync(command.Title, upload.FileId, upload.Length, cancellationToken);

            return Results.Ok();
        })
    .RequireAuthorization("WorkspaceMember");
```

Working on an upload later, outside a request. The id and tenant come from the application's own record, written after `FindAsync` checked the upload:

```csharp
using Headless.MultiTenancy;
using Headless.Tus;

public sealed class VideoProcessor(ITusUploads uploads, ICurrentTenant currentTenant)
{
    public async Task ProcessAsync(string fileId, string? tenantId, CancellationToken cancellationToken)
    {
        using (currentTenant.Change(tenantId))
        {
            // No caller is checked here: never pass an id that came straight from a client.
            var upload = await uploads.FindUncheckedAsync(fileId, cancellationToken)
                ?? throw new InvalidOperationException($"Upload {fileId} is no longer in the store.");

            await using (var content = await upload.OpenContentAsync(cancellationToken))
            {
                // Read the content, for example copy it to its permanent storage.
            }

            // A finished upload never expires: delete it once the application no longer needs it here.
            await upload.DeleteAsync(cancellationToken);
        }
    }
}
```

**Verify:** a cross-origin `POST` from the allowed origin returns `Access-Control-Expose-Headers` listing `Location` and `Upload-Offset`. A creating request that breaks an endpoint rule gets its status from [Response statuses](#response-statuses) with a plain-text reason. The cleanup logs `Removed {Count} expired incomplete upload(s)` at `Information` when a pass removes something, `Expired-upload cleanup pass failed` at `Error` when a pass throws, and `Expired-upload cleanup has nothing to do: store {StoreType} does not support upload expiration` on a store without expiration. `MapHeadlessTus` throws `InvalidOperationException` when the endpoint is not declared.

### Configuration

`TusUploadEndpointOptions`, one instance per endpoint, validated at startup:

| Option | Default | Notes |
| --- | --- | --- |
| `MaxSize` | none (required) | The largest upload, in bytes. Must be positive. Also the request body limit of the endpoint. |
| `Expiration` | 24 hours | How long an incomplete upload stays resumable after its last write (sliding). Past it the upload answers `404` and the cleanup removes it. Must be positive. A store without expiration support ignores it. |
| `AllowedContentTypes` | empty (every type) | Content types an upload may declare, such as `image/png`. An entry turns on the type rule and the first-bytes check. |
| `AllowedFileExtensions` | empty (every extension) | File name extensions an upload may declare, such as `png`, with or without the dot. An entry turns on the extension rule and the first-bytes check. |
| `RequiredMetadata` | empty | Metadata keys every upload must carry with a value, compared by exact case. |
| `AllowEmptyUploads` | `false` | Whether a declared length of zero is accepted. |
| `VerifyFileSignature` | `true` | Whether the first bytes are checked against the two allow-lists. |

No list may hold a blank entry. The options are read on every request and are not stored with an upload, so a change also applies to uploads in progress.

`TusExpiredUploadsCleanupOptions`, set through `tus.ConfigureExpiredUploadsCleanup(options => ...)`:

| Option | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` | Turn it off on hosts that should not sweep, for example when a dedicated worker sweeps for the deployment. |
| `Interval` | 5 minutes | Time between passes. Each pass scans the store, so choose a longer interval for a large container or bucket. Must be positive. |

`TusCorsDefaults` holds the tus CORS surface as lists: `ExposedHeaders`, `AllowedHeaders` (with `Authorization`), and `AllowedMethods` (with the `PATCH` and `DELETE` that default CORS policies miss). `WithTusHeaders()` applies all three. Origins and credentials stay your choice.

### Design and runtime behavior

#### Registration

`AddHeadlessTus` registers the selected store as a singleton under `ITusStore` and its own type, so you can inject `TusS3Store`, `TusAzureStore`, or `ITusStore`. The capability interfaces (`ITusExpirationStore`, `ITusReadableStore`, `ITusTerminationStore`, and the rest) are not registered: cast the store. The container does not dispose an instance passed to `UseStore(store)`, and `UseStore(provider => ...)` builds one from DI. `AddHeadlessTus` also registers `ITusUploads` as a singleton, `IHttpContextAccessor`, and, keeping any the application registered, `TimeProvider.System`, `IFileFormatInspector`, and `IMimeTypeProvider`.

#### Upload endpoints

- **Declare.** `tus.AddUploadEndpoint(options => ...)` declares the default endpoint, `tus.AddUploadEndpoint("videos", options => ...)` a named one. Each takes an `IConfiguration` section, an `Action<TusUploadEndpointOptions>`, or an `Action<TusUploadEndpointOptions, IServiceProvider>`. Names compare by ordinal. Declaring one twice throws `InvalidOperationException`.
- **Shape.** The returned builder takes `OwnedBy(...)`, synchronous or `ValueTask`-returning, and `ValidateWith<TValidator>()` any number of times. A validator resolves from the request's services, so it may depend on scoped services. An unregistered validator type is registered as scoped.
- **Map.** `app.MapHeadlessTus("/files")` maps the default endpoint, `app.MapHeadlessTus("/videos", "videos")` a named one. An upload's URL is the pattern plus its file id. The pattern may hold other route parameters, such as `{workspaceId}`, but not one named `TusFileId`. The call returns `IEndpointConventionBuilder` for `RequireAuthorization(...)` and `RequireCors(...)`. A declared endpoint serves nothing until it is mapped.
- **Mapping fails fast.** `MapHeadlessTus` throws `InvalidOperationException` when `AddHeadlessTus` was not called, the endpoint was not declared, or the endpoint has no `OwnedBy` and no `ICurrentUser` is registered.
- **Requests.** Each request runs with the endpoint's `MaxSize`, a sliding `Expiration`, the registered `ITusFileLockProvider` (tusdotnet's in-process lock when none), and the store's extensions except Concatenation. A request with `Upload-Concat` gets `404`.

#### Isolation

- **The stamp.** At creation the layer stores `headless-endpoint` (the endpoint name, left out for the default), `headless-owner`, and `headless-tenant` (the ambient `ICurrentTenant.Id`, left out with no tenant) after the client's metadata, with or without [tenant scoping](#tenant-scoping). HEAD responses and `TusUpload.Metadata` leave the stamp out. A client key starting with `headless-`, in any case, is refused with `400`.
- **Not found for anyone else.** A request that names a file id is served only when the stamp's endpoint, owner, and tenant equal the caller's. Anyone else gets `404`, identical to an unknown id. That covers another owner or tenant, another endpoint's route, a caller with no tenant, and an upload with no stamp, such as one created through raw `MapTus`.
- **The owner key.** The `OwnedBy` result, or `ICurrentUser.UserId`, read on every request and compared by ordinal. With no key (`null`, empty, or white space) only `OPTIONS` is answered. A caller who is not signed in gets the host's authentication challenge (a bearer scheme answers `401` with `WWW-Authenticate`, a cookie scheme redirects, no authentication answers a plain `401`). A signed-in caller with no key gets `403`. A key over 256 characters is refused at creation with `400`.
- **The layer trusts the key.** With the default owner, authentication already proved it. With `OwnedBy(context => context.Request.RouteValues["workspaceId"]?.ToString())` the key is client input, and anyone who reaches the route can see, resume, and delete that workspace's uploads unless a policy such as `.RequireAuthorization("WorkspaceMember")` stops them. Everyone who presents the same key shares its uploads.
- **Store failures** while reading the stamp answer `503` to a write or delete and `423` to a status request, never `404`, because a tus client told an upload is gone abandons it.
- **Reading through the store** (`ITusReadableStore.GetFileAsync`) shows the `headless-` entries and applies none of these checks. Use `ITusUploads`.
- **Metadata budget.** On `TusAzureStore` the stored metadata, stamp included, is capped at about 7 KB, and a 256-character owner key alone takes about 1 KB. A creating request that no longer fits is refused with `400`.

#### Validation

A creating request is checked before the store creates anything, in this order. The first failure refuses it.

| Rule | Option | Refused with |
| --- | --- | --- |
| The declared length is above the maximum | `MaxSize` | `413` |
| A metadata key holds white space or a control character | always | `400` |
| A required field is missing or has no value | `RequiredMetadata` | `400`, naming the field |
| No content type, or one not in the list | `AllowedContentTypes` (when non-empty) | `415` |
| No file name, no extension, or one not in the list | `AllowedFileExtensions` (when non-empty) | `415` |
| The declared length is zero | `AllowEmptyUploads` is `false` | `400` |
| An application validator fails | `ValidateWith<TValidator>()` | the failure's status and message |

- The content type comes from the `filetype` metadata field, or `type`, ignoring anything after `;`. The file name comes from `filename`, or `name`. Types and extensions compare without regard to case.
- A deferred-length upload is not empty at creation, so `AllowEmptyUploads` does not apply. The write that carries it past `MaxSize` gets `413`.
- Validators run in declaration order after the endpoint's own rules. `ValidateAsync` receives a `TusUploadRequest` (`Length`, `null` when deferred; `Metadata`; `TenantId`; `OwnerKey`; `HttpContext`) and returns `TusUploadValidationResult.Success`, `Failure(message)` (`400`), or `Failure(statusCode, message)` with a status from 400 to 599. A thrown exception fails the request with a server error. `Metadata` is unverified client input.

**The first-bytes check.** While an allow-list has an entry and `VerifyFileSignature` is `true`, the upload's first write is checked against the format its leading bytes show:

- A recognized format passes when a list allows it by content type or extension. An allowed extension also allows its usual content type's format, so `jpeg` allows a file recognized as `jpg`. Any other recognized format gets `415`.
- An unrecognized upload passes when its declared type has no signature, such as `text/csv`, and gets `415` when it has one.
- **Containers only.** Office files are identified by a directory the first write does not carry, so an allowed docx, xlsx, or pptx accepts any ZIP archive, and an allowed doc, xls, or ppt any compound file. Inspect the content when you read the finished upload if you need more.
- **Short signatures match text.** `BM` (BMP), `MZ` (Windows executable), and `ID3` (MP3) are recognized from two or three characters, so a text upload starting with them on a text-only endpoint gets `415`.
- **The first write must be long enough**: a few hundred bytes (the refusal names the count), or the whole upload when shorter. A shorter first write gets `400`. A still-deferred length counts as long. An empty write is not a first write. This matters only for a client with a tiny chunk size.
- **A refused upload is deleted** (`400` or `415`) unless another request finished it meanwhile. When the store cannot delete, it stays until it expires.
- **Creation with upload answers `201`.** When the refused bytes arrive in the creating request, tusdotnet still answers `201` with `Upload-Offset: 0`, and nothing is stored. The client's next write gets the `400` or `415` and deletes the upload.
- **A stalled first write gets `408`** after tusdotnet's `ClientReadTimeout` (60 seconds). Nothing is stored, and the upload stays at offset zero.

#### Finished uploads

An upload is finished once every byte of its declared length is stored. The layer runs nothing then. The client names the file id, the last segment of the upload's URL, in a request of the application's own.

| Member | Returns |
| --- | --- |
| `FindAsync(fileId)` | The default endpoint's finished upload that belongs to the current request's caller, or `null`. |
| `FindAsync(fileId, endpointName)` | The same for a named endpoint. |
| `FindUncheckedAsync(fileId)` | The finished upload with that id, whoever owns it, or `null`. For trusted code outside a request. |

A `TusUpload` carries `FileId`, `Length`, the client's `Metadata`, `EndpointName` (empty for the default endpoint), `TenantId`, `OwnerKey`, `OpenContentAsync(ct)`, and `DeleteAsync(ct)`. `OpenContentAsync` opens a new stream per call and throws `InvalidOperationException` once the upload is gone. Both throw `NotSupportedException` on a store without `ITusReadableStore` or `ITusTerminationStore`.

- **`FindAsync` checks the caller** exactly as the endpoint does (owner from `OwnedBy` or `ICurrentUser`, tenant from `ICurrentTenant`). Its `null` covers an unknown or malformed id, another owner's, tenant's, or endpoint's upload, an unstamped upload, a request with no owner, and an unfinished upload. Answer all the same way.
- **`null` can be momentary.** A lookup holds the upload's file lock while it decides, and returns `null` while another request (a write still verifying, a status request, another lookup) holds it. Retry a `null` if the same id can arrive twice at once.
- **`FindAsync` needs the request.** Outside an HTTP request, or with an undeclared endpoint name, it throws `InvalidOperationException`. When `OwnedBy` reads a route value, give your endpoint the same route value and policy as the upload endpoint.
- **`FindUncheckedAsync` checks no caller.** Pass it an id from `FindAsync` or your own record, or compare `OwnerKey`, `TenantId`, and `EndpointName` yourself. It returns `null` for an unstamped upload. Under tenant scoping, call it and use the result inside `ICurrentTenant.Change(tenantId)`. With no ambient tenant it throws `MissingTenantContextException`.

What happens next is the application's decision: copy the content out and delete it, or keep it in the store as the file's home.

- **A finished upload never expires**, on every store, `TusDiskStore` included.
- **Its client cannot delete it.** A client `DELETE` gets `403`, including a cancel that arrives after the last write landed (tus-js-client's `abort(true)` rejects with it). Treat `403` on cancel as "the upload finished". A client may delete its own unfinished upload. An empty upload, where allowed, is finished from creation.
- **An upload nobody asks for stays.** A signed-in client can leave finished uploads, each up to `MaxSize`, that no request ever names. A create-time validator, such as a storage quota, is the only bound. A storage lifecycle rule cannot tell them from kept uploads.

#### Tenant scoping

The stamp records the ambient tenant, so a caller in another tenant, or none, gets `404` with no further setup. `ScopeByTenant()` also makes a tenant mandatory and separates tenants in storage:

```csharp
using Headless.MultiTenancy;
using Headless.Tus;

builder.AddHeadlessTenancy(tenancy => tenancy.Tus(tus => tus.ScopeByTenant()));
```

- **Answers.** A creating request with no tenant, or a tenant id that cannot be a storage path segment, gets `403` and creates nothing. A valid id starts and ends with a lowercase ASCII letter or digit and holds only those, `.`, `_`, and `-` (`TenantStorageSegment.IsValid`). Any other request in that state gets `404`.
- **Layout.** The `UseS3` and `UseAzure` stores put `.tenants/{tenant}/` before the file id, in a shared bucket or container ([Azure](#headlesstusazure), [S3](#headlesstuss3)). A file id cannot start with `.`, so tenant keys never collide with unscoped ones.
- **Sweep and purge** run with no ambient tenant and cover every tenant and the unscoped uploads.
- **Outside a request**, a scoped store needs `ICurrentTenant.Change(tenantId)` on every call, or throws `MissingTenantContextException`.
- **A store passed to `UseStore`**, including a `TusS3Store` or `TusAzureStore` you construct, keeps tenants side by side, separated by the stamp alone.
- **Switching it on later** does not move uploads. Existing ones answer `404`. Incomplete ones expire into the cleanup, and complete ones stay until the application deletes them.
- **Setup.** The order of `AddHeadlessTus` and the tenancy call does not matter. The posture manifest reports `Tus` with the `scope-by-tenant` capability. Startup fails with `HEADLESS_TENANCY_TUS_NO_STORE` when the seam is recorded and `AddHeadlessTus` was never called. [Multi-Tenancy](multi-tenancy.md) covers how a request gets its tenant.

#### Request body limits

tus-js-client and Uppy send a whole upload in one PATCH by default, and Kestrel's default body limit is 30 MB. `MapHeadlessTus` sets the body limit of every request to the endpoint to `MaxSize`, replacing the server's limit, unless the server exposes no such limit or no longer lets it change.

Limits in front of the application cannot be lifted from code: a reverse proxy (nginx `client_max_body_size`, 1 MB by default), a gateway or CDN, and IIS request filtering (`maxAllowedContentLength`, about 28.6 MB by default). Raise them, or set a client `chunkSize` below the lowest limit on the path.

#### Response statuses

Every refusal is a tus response with a plain-text body and `Tus-Resumable`. A refused HEAD also carries `Cache-Control: no-store`. The layer writes no `ProblemDetails`.

| Status | When |
| --- | --- |
| `400` | Missing required metadata; an empty upload where none is allowed; a `headless-` metadata key; an owner key over 256 characters; metadata over the store's limit; a first write too short for the first-bytes check; a validator failure with no status. |
| `401` | No owner key and nobody signed in. A registered authentication scheme's challenge sets the response instead. |
| `403` | No owner key and the caller is signed in; tenant scoping with no tenant or an invalid tenant id on create; a client `DELETE` of a finished upload. |
| `404` | Unknown file id; another endpoint's, owner's, or tenant's upload; no stamp; an expired upload; `Upload-Concat`. |
| `408` | The first write's first bytes stopped arriving before `ClientReadTimeout`. |
| `413` | The declared length, or the bytes that arrive, exceed `MaxSize`. |
| `415` | The declared type or extension is missing or not allowed; the first bytes are not an allowed or declared format. |
| `423` | Another request holds the upload's lock; the store failed while a status request was authorized. |
| `503` | The store failed while a write or delete was authorized. |

A validator can answer any status from 400 to 599. A validator exception answers `500` unless your exception handling says otherwise. tusdotnet's protocol errors keep their statuses.

#### Raw MapTus

tusdotnet's `MapTus` works beside `MapHeadlessTus` for a `DefaultTusConfiguration` you build by hand. It gets none of the layer's checks: no owner or tenant check (anyone holding a file id can read, resume, or delete it), no validation or delete rule beyond what the configuration sets, and no lock provider or body limit unless the factory sets them (tusdotnet then locks within the process, and Kestrel's 30 MB limit applies). Concatenation stays available when configured. A `UseS3` store learns an upload's sliding window from the first refresh it sees, so give every raw endpoint over an S3 store one fixed expiration policy.

The two do not mix over one store. `MapHeadlessTus` and `ITusUploads` never find a raw upload, unless its client sent `headless-` entries of its own choosing, which a raw endpoint does not refuse. A raw endpoint serves a `MapHeadlessTus` upload to anyone who holds its id and echoes its stamp in HEAD. With tenant scoping on, a `UseS3` or `UseAzure` store still scopes a raw request by tenant and throws `MissingTenantContextException` with none.

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
        MaxAllowedUploadSizeInBytesLong = 100L * 1024 * 1024,
        Expiration = new SlidingExpiration(TimeSpan.FromHours(1)),
        // Registered by UseDistributedLocks; leave it unset on a single node.
        FileLockProvider = httpContext.RequestServices.GetRequiredService<ITusFileLockProvider>(),
    }));
```

#### Expired-upload cleanup

tusdotnet only reports `Upload-Expires`. `AddHeadlessTus` registers a job, on by default, that removes expired uploads from the store.

- **Incomplete uploads only.** A Headless store never reports a finished upload as expired.
- **Schedule.** The first pass runs at host start, reclaiming uploads that expired while it was down, then one per `Interval`. A failed pass, for example before the bucket is provisioned, is logged and the next tick runs normally. A store without `ITusExpirationStore` gets no passes.
- **Several nodes.** With `UseDistributedLocks`, a node runs a pass only when it takes the `{resourcePrefix}:expired-uploads-cleanup` lease, held for 90% of the interval, so the cluster sweeps about once per interval, and a crashed node delays cleanup by at most one interval. Without it, every node sweeps every tick: safe, since deletes are idempotent, but the scan load multiplies and counts are per node.
- **Races.** The job holds no file lock. Each Headless store deletes an upload only when it is unchanged since the pass found it, so a resume that committed data or refreshed the expiration in between survives. A resume that has started but written nothing can still lose its upload. `UseDistributedLocks` closes that window.

---

## Headless.Tus.Azure

`TusAzureStore` stores each upload as an Azure block blob. It supports Creation, Creation Defer Length, Concatenation, Expiration, Checksum (SHA1, SHA256, SHA512, MD5), Termination, and reading a file back (`GetFileAsync`).

### Setup

```bash
dotnet add package Headless.Tus.Azure
```

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

Create the container as a deploy step: `dotnet MyApp.dll resources provision -k AzureBlob`.

`UseAzure` takes an `IConfiguration` section, an `Action<TusAzureStoreOptions>`, or an `Action<TusAzureStoreOptions, IServiceProvider>`. It needs a `BlobServiceClient` in DI, uses an `ITusAzureBlobHttpHeadersProvider` or `ITusFileIdProvider` the application registered, adds `IHttpContextAccessor`, and registers the `AzureBlob` provisioner named after `ContainerName`.

A store you construct yourself goes through `UseStore`, with no provisioner and no per-tenant layout. Pass it `httpContextAccessor` to read each blob once per tus request:

```csharp
using Azure.Storage.Blobs;
using Headless.Tus;

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseStore(provider => new TusAzureStore(
        provider.GetRequiredService<BlobServiceClient>(),
        new TusAzureStoreOptions { ContainerName = "uploads", BlobPrefix = "tus/" },
        httpContextAccessor: provider.GetService<IHttpContextAccessor>()));

    tus.AddUploadEndpoint(options => options.MaxSize = 1024L * 1024 * 1024);
});
```

The constructor is `new TusAzureStore(blobServiceClient, options, blobHttpHeadersProvider, fileIdProvider, loggerFactory, timeProvider, httpContextAccessor)`. Every parameter after `options` is optional, and invalid options throw `ValidationException`.

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

// UseAzure keeps a provider the application registered.
builder.Services.AddSingleton<ITusAzureBlobHttpHeadersProvider, MyHeadersProvider>();
```

The headers apply at creation and on every later commit, so a custom content type survives each PATCH.

### Configuration

| `TusAzureStoreOptions` | Default | Notes |
| --- | --- | --- |
| `ContainerName` | `"tus-uploads"` | Azure Blob container. Required. |
| `BlobPrefix` | `"uploads/"` | Prefix of every blob name the store writes. |
| `ContainerPublicAccessType` | `PublicAccessType.None` | Access type `resources provision` applies when it creates the container. Ignored for an existing one. |
| `EnableChunkSplitting` | `true` | Splits a PATCH body into several Azure blocks. |
| `BlobDefaultChunkSize` | 4 MB | Block size for medium uploads. 1 byte to 100 MB, and at most `BlobMaxChunkSize`. |
| `BlobMaxChunkSize` | 16 MB | Block size for uploads of 100 MB and more, and the per-request buffer for them. At most 100 MB, a memory cap the store sets (Azure allows 4,000 MiB per block). |
| `MaxNoSplitBufferSize` | 256 MB | With `EnableChunkSplitting = false`, the largest PATCH body the store buffers. 1 byte to about 2 GB. |
| `DeletePartialFilesOnConcat` | `false` | Deletes partial uploads after a final upload commits, best effort. Keep `false` when clients reuse partials. |

Block size by declared length: under 10 MB, `min(BlobDefaultChunkSize, length)`; 10 MB to 100 MB, `BlobDefaultChunkSize`; 100 MB and more, `BlobMaxChunkSize`.

### Design and runtime behavior

**Upload limits.**

- An upload holds at most 50,000 committed blocks, Azure's limit. A block size too small for a very large upload throws `TusStoreException` at commit. Raise the chunk sizes for fewer, larger blocks.
- With `EnableChunkSplitting = false`, the whole PATCH body is buffered and staged as one block. For a deferred-length upload, `MaxNoSplitBufferSize` and the request body limit are the only memory bounds. A larger body fails with `TusStoreException` (`400`). For a declared length, tusdotnet answers an over-length PATCH with `413` before buffering.
- `Upload-Metadata` is stored verbatim and returned byte for byte on HEAD. A value that is not printable ASCII, or metadata over about 7 KB (Azure allows 8 KB of blob metadata), fails with `TusStoreException`. A concatenation final also stores its partial ids, and `CreateFinalFileAsync` throws `TusStoreException` when they would pass 8 KB.
- A file id must not start with `.`, contain `/`, `\`, `..`, `,`, or a control character, or start or end with whitespace. Every entry point rejects it with `TusStoreException`, besides the id provider's own `ValidateId`.

**Offsets and checksums.** The offset is the blob's committed length, so staged blocks never advance it. Both `Upload-Checksum` forms use a constant-time comparison. In the header form, blocks commit only after the checksum passes; on a mismatch they stay uncommitted and Azure removes them within 7 days. In the trailer form (`checksum-trailer`), data commits during the append, and a mismatch or a missing or faulty trailer rolls the chunk back.

**Disconnects.** Reading the body follows the request token. Storing bytes already received runs to completion: a disconnected PATCH commits what arrived, and the expiration refresh and checksum verification run after the request token is cancelled. A checksum chunk that fully arrived and verified before the disconnect is committed. Expired-file deletion finishes once a file is enumerated, and a cancelled enumeration returns the files found so far.

**Expiration.** `GetExpirationAsync` returns `null` for a completed upload, so HEAD still reaches it after its window.

**Write fencing.** Every block-list commit and metadata write is conditioned on the blob ETag the request read. A stale request fails with `Azure.RequestFailedException` status `412`, which tusdotnet answers with `500` and the client retries. The expiration refresh instead re-reads and retries up to three times, so it does not fail a PATCH whose data already committed. A concatenated final blob is written only when no blob has its name. A tus `DELETE` and the deletion of partials are unconditional; the cleanup's deletes are conditional.

**Storage requests.** With the `IHttpContextAccessor` that `UseAzure` adds, the store reads a blob once per tus request: a HEAD costs one request, a creation with sliding expiration two, a PATCH five, a checksum-header PATCH six. The finished check made by the delete rule and `ITusUploads` reads the blob again. Without an accessor the store reads on every call and stays correct.

**Concatenation.** The final upload copies blocks server-side. When the server cannot (Azurite answers `501`; a private container answers `403` because the source has no SAS), the store downloads and re-uploads the data.

**Tenant layout.** Under [tenant scoping](#tenant-scoping), the blob is `{BlobPrefix}.tenants/{tenant}/{id}`, for example `uploads/.tenants/acme/{id}`. The cleanup lists the whole prefix once and reads each upload's tenant from its blob name.

**Provisioning.** `resources provision` creates the container with `ContainerPublicAccessType`, so the serving host needs no container-create permission. A starting host checks the container with one request and holds the `warmup` readiness check until it exists; change that per kind with `ResourceProvisioningOptions.StartupChecks`. `resources purge` deletes the blobs under `BlobPrefix`, every tenant's included, and keeps the container. `resources delete` also deletes the container, only when nothing else is left in it.

---

## Headless.Tus.S3

`TusS3Store` stores each upload as one S3 multipart upload, in Cloudflare R2, Amazon S3, or an S3-compatible server such as MinIO. It supports Creation (with upload), Creation Defer Length up to a cap, Concatenation, Expiration, Termination, and reading a file back. It does not support Checksum, so tusdotnet does not advertise it. Use `Headless.Tus.Azure` when clients send `Upload-Checksum`.

### Setup

```bash
dotnet add package Headless.Tus.S3
```

Register `IAmazonS3` for your server, then select the store. Cloudflare R2:

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
        // Below R2's 7-day multipart upload window. 24 hours is also the default.
        options.Expiration = TimeSpan.FromDays(1);
    });
});

var app = builder.Build();

app.MapHeadlessTus("/files").RequireAuthorization();

app.Run();
```

Amazon S3 takes `new AmazonS3Client()`, with credentials and region from the SDK's default chain, and the default `Provider = TusS3Provider.Aws`. MinIO or another S3-compatible server takes a client configured as the [client settings](#s3-client-settings) table says, and options from configuration:

```csharp
using Headless.Tus;

builder.Services.AddHeadlessTus(tus =>
{
    tus.UseS3(builder.Configuration.GetSection("Tus"));
    tus.AddUploadEndpoint(options => options.MaxSize = 5L * 1024 * 1024 * 1024);
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

Create the bucket as a deploy step: `dotnet MyApp.dll resources provision -k S3Bucket`.

`UseS3` takes an `IConfiguration` section, an `Action<TusS3StoreOptions>`, or an `Action<TusS3StoreOptions, IServiceProvider>`. It resolves `IAmazonS3` from DI and does not configure it, uses an `ITusS3ObjectHeadersProvider` or `ITusFileIdProvider` the application registered, adds `IHttpContextAccessor`, and registers the `S3Bucket` provisioner named after `Bucket`. A store you construct, `new TusS3Store(s3, options, objectHeadersProvider, fileIdProvider, loggerFactory, timeProvider, httpContextAccessor)`, goes through `UseStore` with no provisioner and no per-tenant layout. Every parameter after `options` is optional, and invalid options throw `ValidationException`.

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

// UseS3 keeps a provider the application registered.
builder.Services.AddSingleton<ITusS3ObjectHeadersProvider, DownloadHeadersProvider>();
```

`TusS3ObjectHeaders` carries `ContentType`, `CacheControl`, `ContentDisposition`, `ContentEncoding`, and `ContentLanguage`. They are fixed when the multipart upload starts and cannot change without copying the object. Metadata values are client input: validate or encode them before they become stored response headers.

### Configuration

| `TusS3StoreOptions` | Default | Notes |
| --- | --- | --- |
| `Bucket` | `""` | Required. |
| `Prefix` | `"uploads/"` | The key prefix the store owns. Must not be empty. A missing trailing `/` is added. Share it with no other writer, because `resources purge` deletes objects there. |
| `Provider` | `TusS3Provider.Aws` | `Aws`, `S3Compatible`, or `R2`. Sets per-request settings and, for `R2`, the default `MultipartUploadLifetime`. |
| `PartSize` | 8 MiB | Part size for a deferred-length upload, and the minimum for a known length. At least 5 MiB. Sets the deferred-length cap. |
| `MaxPartSize` | 64 MiB | Largest part size for a long known-length upload. At least `PartSize`, at most 1 GiB. Sets the largest accepted length. |
| `MaxConcurrentPartUploads` | `2` | Parts one PATCH uploads in parallel. At least 1. |
| `PartClaimWindow` | `64` | Part numbers one state write reserves. 1 to 250. A wider window lowers both length limits. |
| `MaxTailSegments` | `16` | Tail segments an upload holds before the store merges them. At least 1. |
| `PersistTimeout` | 30 seconds | How long the store keeps storing received bytes after a client disconnects. Must be positive. Keep it below the distributed lock lease. |
| `MultipartUploadLifetime` | `null` (7 days with `Provider = R2`) | When the server aborts an incomplete multipart upload. Set it to match a bucket abort rule. Must be positive when set. |

### Design and runtime behavior

#### S3 client settings

The app registers `IAmazonS3`, and registration does not check its settings:

| Server | `ServiceURL` | `ForcePathStyle` | `AuthenticationRegion` | Checksum settings | `Provider` |
| --- | --- | --- | --- | --- | --- |
| Cloudflare R2 | `https://<account-id>.r2.cloudflarestorage.com` | `true` | `auto` | `WHEN_REQUIRED` (required) | `R2` |
| Amazon S3 | SDK default | SDK default | SDK default | SDK default | `Aws` |
| MinIO or another S3-compatible server | The server URL; `http` allowed | `true` | The server's region, often `us-east-1` | `WHEN_REQUIRED` (most servers reject the SDK's default CRC checksums) | `S3Compatible` |

`Provider` changes only per-request settings. Every write sends a plain `Content-Length` body. `R2` sends unsigned payloads, skipping a hash pass over every part. `S3Compatible` keeps payloads signed, because the SDK refuses unsigned payloads over plain `http`. `Aws` keeps the SDK default.

Set the client's `Timeout` and `MaxErrorRetry` deliberately. Completion, the expiry refresh, and the cleanup removals ignore cancellation, so the client's timeout and retry count are the only bound on a call to a slow or unreachable server.

R2 is tested against a real R2 bucket, Amazon S3 against LocalStack. MinIO is supported as an S3-compatible server; its upstream repository was archived in April 2026.

**Object layout.** For file id `{id}`: `{Prefix}{id}` is the finished object; `{Prefix}.tus/{id}/info` is the JSON state of an incomplete upload (length, part size, metadata, expiry, committed parts); `{Prefix}.tus/{id}/seg/...` holds tail segments, bytes that do not yet fill a part; `{Prefix}.tus-completed/{id}` is the record a completed upload keeps. Under [tenant scoping](#tenant-scoping) these become `{Prefix}.tenants/{tenant}/{id}`, `{Prefix}.tus/.tenants/{tenant}/{id}/...`, and `{Prefix}.tus-completed/.tenants/{tenant}/{id}`. The tus metadata lives in the state object, so S3's 2 KB user-metadata limit does not apply. The store rejects a file id that starts with `.`, contains `/`, `\`, or a control character, or starts or ends with whitespace, with `TusStoreException`.

**Length limits.** S3 and R2 allow 10,000 parts per multipart upload, and R2 needs every part except the last to be the same size. The store fixes the part size at creation and holds four claim windows in reserve, so an upload spans at most `10,000 − 4 × PartClaimWindow` parts: 9,744 at the defaults.

- **Known length.** The part size is the larger of `PartSize` and the length divided by the part budget, rounded up to 1 MiB. The largest accepted length is the part budget × `MaxPartSize`: 623,616 MiB (about 609 GiB) at the defaults. Creation rejects a longer upload with `TusStoreException` (`400`).
- **Deferred length.** The part budget × `PartSize`: 77,952 MiB (about 76 GiB) at the defaults. Declaring or appending past it fails with `TusStoreException`. Raising `PartSize` raises the cap and the memory each PATCH holds.

**Memory.** A client may send chunks of any size, below 5 MiB included: bytes that do not fill a part become tail segments, read back into the next part. One PATCH holds at most `MaxConcurrentPartUploads` + 1 part buffers, each the part size rounded up to a power of two (6 MiB parts take 8 MiB buffers). The store writes nothing to local disk.

**Disconnects.** After a disconnect the store still stores every byte it received, bounded by `PersistTimeout`. Keep `PersistTimeout` below the `IDistributedLock` lease, or a lock failover can start a second writer while the first is still storing, and the write fencing fails one of them.

**Write fencing.** Creation writes the state object only when it does not exist, and every later write is conditioned on the ETag the request read. A stale request fails with `AmazonS3Exception` (`412 PreconditionFailed`, `409 ConditionalRequestConflict`, or `404 NoSuchKey`), which tusdotnet answers with `500`, and the client retries from HEAD. A PATCH reserves its part numbers before uploading any part, so a stale writer fails before it can replace a part on R2.

**Completion.** The final PATCH completes the multipart upload, writes the completed record, and deletes the state object. A zero-length upload writes an empty finished object at creation. If completion is interrupted, HEAD reports the offset from before the final PATCH; the client sends it again, the store finishes, and tusdotnet raises `OnFileCompleteAsync`. If the cleanup claimed the upload first, the final PATCH fails with a retryable `500` and leaves no finished object. Concatenation builds the final upload inside `CreateFinalFileAsync`, copies parts server-side, accepts partials of any size, and keeps the partials.

**Aborted multipart uploads.** R2 aborts an incomplete multipart upload 7 days after creation by default. With `Provider = R2` and no `MultipartUploadLifetime`, the store applies the same 7 days: once they pass since the first part, the upload reads as missing (`404`) and the cleanup removes it. An upload whose final PATCH already completed the multipart upload stays reachable. Finish an R2 upload within 7 days of its first part, and keep the endpoint's `Expiration` below that. When the server reports the multipart upload gone (`NoSuchUpload`), the store answers with `TusStoreException`, never a corrupt upload. The store never edits bucket lifecycle rules: when a rule changes R2's window, or a bucket elsewhere has an abort rule, set `MultipartUploadLifetime` to match.

**Provisioning.** `resources provision -k S3Bucket` creates the bucket, and a bucket the account already owns counts as created. On R2 that step needs a token that can create buckets; the serving host needs only object read, write, and list rights. A starting host checks the bucket with one `ListObjectsV2` call and holds the `warmup` readiness check until it exists. `resources purge` deletes the store's objects under `Prefix` (working sets, completed records, and the finished objects they name, every tenant's included) and keeps the bucket and every other object. `resources delete` does the same, then deletes the bucket only when nothing else is left in it.

**Storage requests.** With the `IHttpContextAccessor` that `UseS3` adds, the store reads an upload's state once per tus request. Without one it reads on every call and stays correct. R2 and S3 bill per operation:

| tus operation | Storage requests |
| --- | --- |
| HEAD, incomplete upload | 1 |
| HEAD, completed upload | 2 |
| Creation | 1, plus 1 when expiration is on |
| PATCH that only adds a tail segment | 3 |
| PATCH that writes N parts | N + 4 + ⌈N / 64⌉, plus 1 per tail segment read back, plus 1 on the first part-writing PATCH |
| Final PATCH, on top of its append | 4, plus 1 delete |
| Cleanup pass | 1 listing per 1,000 in-progress uploads and 1 read per in-progress upload, plus 1 listing when a tenant has an upload in progress. A pass that removes uploads adds 1 multipart-upload listing page and the deletes. Completed uploads cost nothing. |

Under sliding expiration, the first writing PATCH after creation costs one more state write to refresh the expiry.

**Cleanup.** `RemoveExpiredFilesAsync` lists every tenant's in-progress uploads, reads each state object, and skips an upload a request changed since the read. An upload stuck mid-completion is removed, with its finished object, only once it is past its earlier expiry and has been completing for over an hour. The pass also aborts a leaked multipart upload, older than one hour, on the key of an upload it read. MinIO lists multipart uploads only by exact key, so there the pass finds none; a tus `DELETE` still aborts them, and a bucket abort rule covers the rest.

**Reading uploads.** `GetFileAsync` returns `null` for an unknown or removed upload. For an incomplete upload it returns the metadata, but `GetContentAsync` throws `InvalidOperationException`, because S3 does not serve the parts of an incomplete multipart upload. A finished upload streams from the finished object, through `GetContentAsync` inside `OnFileCompleteAsync` or through `TusUpload.OpenContentAsync`.

---

## Headless.Tus.DistributedLocks

`UseDistributedLocks` registers an `ITusFileLockProvider` over `IDistributedLock`: one request per upload across nodes, cross-node `ITusUploads` lookups, and the cleanup on one node per interval.

### Setup

```bash
dotnet add package Headless.Tus.DistributedLocks
```

```csharp
using Headless.DistributedLocks;
using Headless.Tus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. A Headless distributed lock backend (Redis shown; any backend works).
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

// 3. The mapped endpoint uses the lock provider with no further wiring.
app.MapHeadlessTus("/files").RequireAuthorization();

app.Run();
```

A raw `MapTus` endpoint does not pick the provider up: set `FileLockProvider` in its factory ([Raw MapTus](#raw-maptus)).

**Verify:** start two PATCH requests for the same upload at once. The second gets `423 Locked`. Resolving `ITusFileLockProvider` throws when no `IDistributedLock` is registered.

### Configuration

`UseDistributedLocks(string resourcePrefix = "tus-file-lock")` is the only setting. An upload's lock resource is `{resourcePrefix}-{fileId}`, and the cleanup lease is `{resourcePrefix}:expired-uploads-cleanup`. When several applications share one `IDistributedLock` backend, give each its own prefix so equal file ids do not contend.

### Design and runtime behavior

- `ITusFileLockProvider` is a singleton. `IDistributedLock` resolves once the host is built, so the order of `AddHeadlessDistributedLocks` and `AddHeadlessTus` does not matter.
- A lock attempt does not wait. When another node holds the upload, tusdotnet answers `423 Locked`, and `ITusUploads.FindAsync` returns `null`.
- The lease does not expire while held and extends itself. A crashed holder's lease expires, freeing the upload.
- The lock gives mutual exclusion, not fencing. A holder that loses its lease during a backend partition keeps writing until its request ends, and the store's write fencing fails its write with a retryable `500`. What the application does with a finished upload has no such fence, so keep that step safe to repeat.
