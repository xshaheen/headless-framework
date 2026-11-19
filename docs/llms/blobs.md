---
domain: Blob Storage
packages: Blobs.Abstractions, Blobs, Blobs.MultiTenancy, Blobs.Aws, Blobs.Azure, Blobs.CloudflareR2, Blobs.FileSystem, Blobs.Redis, Blobs.SshNet, Blobs.SignedUrlEndpoint
---

# Blob Storage

> One `IBlobStorage` contract over Amazon S3 and S3-compatible servers, Azure Blob Storage, Cloudflare R2, the local file system, Redis, and SFTP, with optional container management, presigned URLs, and tenant scoping.

## Orientation

Install `Headless.Blobs` and one or more provider packages. `Headless.Blobs` brings `Headless.Blobs.Abstractions`. Register every store in one `AddHeadlessBlobs(...)` call: pick a default store with `Use{Provider}(...)`, and add named stores with `AddNamed(name, i => i.Use{Provider}(...))`. Application code depends on `IBlobStorage` only.

| Need | Package and call |
| --- | --- |
| Local development and tests, or single-node on-premises | `Headless.Blobs.FileSystem`, `UseFileSystem` |
| Amazon S3 | `Headless.Blobs.Aws`, `UseAws` |
| MinIO, Ceph RGW, Garage, or another self-hosted S3-compatible server | `Headless.Blobs.Aws`, `UseS3Compatible` |
| Azure Blob Storage | `Headless.Blobs.Azure`, `UseAzure` |
| Cloudflare R2 | `Headless.Blobs.CloudflareR2`, `UseCloudflareR2` |
| An SFTP server or a legacy system | `Headless.Blobs.SshNet`, `UseSsh` |
| Small, short-lived blobs (default limit 10 MB) | `Headless.Blobs.Redis`, `UseRedis` |
| Presigned URLs on FileSystem, Redis, or SFTP | `Headless.Blobs.SignedUrlEndpoint`, `UseSignedUrlEndpoint` |
| Blobs isolated per tenant | `Headless.Blobs.MultiTenancy`, `ScopeByTenant` |
| A backend no shipped provider covers | Your own `IBlobStorage`; see [Write a blob storage provider](#write-a-blob-storage-provider) |

The default store resolves as plain `IBlobStorage`. A named store resolves as keyed `IBlobStorage` or through `IBlobStorageProvider`. Every operation addresses a blob with one `BlobLocation(container, path)` value. Container management (`IBlobContainerManager`) and presigned URLs (`IPresignedUrlBlobStorage`) are optional capabilities outside `IBlobStorage`.

| Provider | `IBlobContainerManager` | `IPresignedUrlBlobStorage` | Metadata storage | Listing tier |
| --- | --- | --- | --- | --- |
| AWS S3, S3-compatible | Yes | Native | Native | Server-side stable |
| Azure | Yes | Native (SAS) | Native | Server-side stable |
| Cloudflare R2 | No | Native | Native | Server-side stable |
| FileSystem | Yes | With `UseSignedUrlEndpoint` | Sidecar file | Emulated re-scan |
| SFTP (`SshNet`) | Yes | With `UseSignedUrlEndpoint` | Sidecar file | Emulated re-scan |
| Redis | Yes (no-op create) | With `UseSignedUrlEndpoint` | Native | Cursor scan |

## Agent Rules

- Depend on `IBlobStorage`. Never reference a provider's concrete storage type in application code.
- Call `AddHeadlessBlobs` once. A second call on the same service collection throws. Put the default store, every named store, and `UseSignedUrlEndpoint` inside that one call.
- Address every blob with `BlobLocation`. Pass it through as is: the constructor validates the path once, and the provider applies its own naming rules. Do not normalize paths yourself. See [Addressing with BlobLocation](#addressing-with-bloblocation).
- Create the top-level container before the first upload. `UploadAsync` never creates a missing bucket or container and throws instead. Call `IBlobContainerManager.EnsureContainerAsync` at deploy time, or provision it with infrastructure as code. `IBlobStorage.RequiresContainerProvisioning` is `false` only for Redis.
- Resolve `IBlobContainerManager` from DI (`GetService` or `[FromKeyedServices("name")]`), and null-check it. Never cast the store to it. Cloudflare R2 registers none.
- Detect presigned support with a cast on the store: `storage is IPresignedUrlBlobStorage presigned`. For a named store you can also inject keyed `IPresignedUrlBlobStorage`. There is no unkeyed `IPresignedUrlBlobStorage` registration, so never inject it without a key.
- Check `IPresignedUrlBlobStorage.SupportedUploadConstraints` before you rely on an upload URL's `MaxLength` or `ContentType`. A backend silently ignores a constraint it cannot enforce. Validate the uploaded blob yourself for any constraint not in the flags.
- Pass the cancellation token by name to the presign methods: `GetPresignedDownloadUrlAsync(location, expiry, cancellationToken: ct)`. The third positional parameter is the options object.
- Pass the content type in the `contentType` argument of `UploadAsync` or `BlobUploadRequest.ContentType`. Never put a `content-type` key in metadata: S3 stores it as the user header `x-amz-meta-content-type` and still serves the type it derives from the key.
- Dispose every `BlobDownloadResult` from `OpenReadStreamAsync` with `await using`, promptly. An undisposed result holds a connection, and on SFTP it blocks later downloads once the read-stream pool is used up.
- Treat a continuation token as opaque. Never parse, compare, or store it across a provider change. A malformed or forged token throws `ArgumentException`.
- Expect `MoveAsync` to return `false` when the destination exists. Delete the destination first, or call `CopyAsync` to overwrite.
- In a multi-tenant host, scope blobs with `Headless.Blobs.MultiTenancy` instead of building tenant ids into paths. See [Tenant scoping](#tenant-scoping).

## Core Concepts

### Addressing with BlobLocation

`BlobLocation` is a `readonly record struct` with a `Container` and a `Path`:

- `Container` is the top-level store root: the S3 or R2 bucket, the Azure container, a directory under the FileSystem or SFTP root, or a Redis key prefix. The provider applies its backend's strict naming rules to it (lowercase, length, allowed characters; R2 also forbids dots).
- `Path` is the container-relative object key. It may contain `/`. The provider validates it and does not rewrite it.

The constructor throws `ArgumentException` for path traversal, an absolute path, a control character, or a path segment that ends in the reserved `.hlmeta` sidecar suffix. Three constructors:

```csharp
var a = new BlobLocation("uploads", "images/a.png");          // container, path
var b = new BlobLocation("uploads", "images", "a.png");       // container, segments joined with "/"
var c = new BlobLocation(["uploads", "images", "a.png"]);     // first segment is the container; needs at least two
```

All three address the key `images/a.png` in container `uploads`. `MoveAsync` and `CopyAsync` take a source and a destination `BlobLocation`, and may cross containers within one store.

### Listing: token paging and guarantee tiers

`ListAsync(BlobQuery)` returns a `BlobPage(Items, ContinuationToken)`. A `null` `ContinuationToken` marks the last page. To fetch the next page, pass the token to a new `BlobQuery(container, prefix, pageSize, continuationToken)`. The page size defaults to 100. The token is a string that survives a web request, so you can hand it to a client as a pagination cursor.

For a full enumeration, use the extensions: `GetBlobsAsync(query)` streams every page as `IAsyncEnumerable<BlobInfo>`, and `GetBlobsListAsync(query, limit)` materializes a list.

| Tier | Providers | Behavior under concurrent writes | Cost |
| --- | --- | --- | --- |
| Server-side stable | AWS S3, S3-compatible, Cloudflare R2, Azure | Stable | One request per page |
| Emulated re-scan | FileSystem, SFTP | A blob written before the resume point can be skipped or repeated | Each page re-scans the container from the start, so a full enumeration costs O(n²/pageSize) I/O. Memory stays O(pageSize). |
| Cursor scan | Redis | Order is not by key, and a blob can appear twice across a rehash. Tolerate duplicates. | One `HSCAN` per page |

A token from a different provider or store is not detected. The result is backend-defined, typically an empty page or a backend error.

### Prefix vs. glob filtering

`BlobQuery.Prefix` is the only filter the backend applies. It is validated like a `BlobLocation` path, so a `../` prefix throws at construction.

Glob patterns (`*`, `?`) run in the client: `GetBlobsAsync(query, globPattern)` narrows the listing with the literal prefix of the pattern when it can, then filters each result. `DeleteAllAsync(BlobQuery)` deletes by prefix and returns the count. To delete by glob, list with the glob, then call `BulkDeleteAsync`.

### Capabilities: container management and presigned URLs

**Container management.** `IBlobContainerManager` has `EnsureContainerAsync` (idempotent create), `ContainerExistsAsync`, and `DeleteContainerAsync`. Each provider that supports it registers it as a separate service: unkeyed for the default store, keyed by name for a named store. Cloudflare R2 registers none, because R2 tokens are object-scoped and cannot create buckets, so resolution returns `null` for an R2 store. FileSystem and SFTP still create the intermediate directories a blob path needs; only the top-level container must exist.

A `null` manager is a misconfiguration only when the store reports `RequiresContainerProvisioning == true`.

**Presigned URLs.** `IPresignedUrlBlobStorage` mints time-limited URLs for a `BlobLocation`: `GetPresignedDownloadUrlAsync(location, expiry, options, ct)` and `GetPresignedUploadUrlAsync(location, expiry, constraints, ct)`. A presigned URL stays reusable until it expires. Single-use URLs are not offered.

Upload constraints (`PresignedUploadConstraints`):

| Backend | `ContentType` | `MaxLength` |
| --- | --- | --- |
| AWS S3, S3-compatible, Cloudflare R2 | Enforced. The uploader must send that exact `Content-Type`. | Ignored |
| Azure | Ignored | Ignored |
| `Headless.Blobs.SignedUrlEndpoint` | Enforced | Enforced |

```csharp
var url = await presigned.GetPresignedUploadUrlAsync(
    location,
    TimeSpan.FromMinutes(15),
    new PresignedUploadConstraints { ContentType = "image/png", MaxLength = 2_000_000 },
    ct
);

if (!presigned.SupportedUploadConstraints.HasFlag(PresignedUploadConstraintKinds.MaxLength))
{
    // After the client reports the upload done: check BlobInfo.Size and delete an oversized blob.
}
```

Download options (`PresignedDownloadOptions`) set the response's `Content-Disposition` (`FileName`) and `Content-Type` (`ContentType`). Every presigning backend honors both, and the values are signed into the URL, so its holder cannot change them. `FileName` is sent as `attachment; filename="<ASCII fallback>"; filename*=UTF-8''<percent-encoded name>`, so a non-ASCII name such as Arabic reaches the browser intact. In the fallback, every non-ASCII character, quote, backslash, and percent sign becomes `_`. The setters throw `ArgumentException` for a blank file name, a control character in either value, or a content type that is not one media type (`image/*` is refused). With `null` options, or both members `null`, the URL is the same as one minted without options.

```csharp
var url = await presigned.GetPresignedDownloadUrlAsync(
    location,
    TimeSpan.FromMinutes(15),
    new PresignedDownloadOptions { FileName = "تقرير مارس.pdf", ContentType = "application/pdf" },
    ct
);
```

### Metadata and sidecar companions

Metadata is `IReadOnlyDictionary<string, string>?` with non-null values everywhere: the `UploadAsync` parameter, `BlobInfo.Metadata`, and `BlobDownloadResult.Metadata`. Every provider round-trips it. S3, Azure, and Redis store it natively. FileSystem and SFTP store it in a sidecar file beside the blob, named with the reserved `.hlmeta` suffix.

- Listings never show sidecars, in results, existence checks, counts, or prefix and glob matches.
- The blob is written first, then the sidecar. The pair is not atomic, and a missing sidecar reads as empty metadata.
- Deleting or moving a blob deletes or moves its sidecar, so re-uploading a key without metadata never returns stale metadata.

**Listings omit metadata by default.** `BlobInfo.Metadata` is `null` in every listing unless you construct the query with `includeMetadata: true`. Opting in costs one `HeadObject` per key on S3 (parallel up to `MaxBulkParallelism`), one sidecar read per key on FileSystem and SFTP, and nothing extra on Azure and Redis. `GetBlobInfoAsync` is the authoritative source for one blob.

**Content type.** When `contentType` is `null`, the provider derives the type from the key's extension, so a key without an extension is served as `application/octet-stream`. S3, Azure, and R2 store the type as the object's `Content-Type`, which every GET and presigned URL carries. FileSystem, Redis, and SFTP do not store it.

`BlobDownloadResult.FileName` is the full container-relative key on every provider. `BlobDownloadResult.ContentLength` and `ContentType` come with the download, so you can size a buffer without a separate `GetBlobInfoAsync`:

| Provider | `ContentLength` | `ContentType` |
| --- | --- | --- |
| S3, R2, Azure | Set | Set, or `null` when none was stored |
| FileSystem, Redis | Set | `null` |
| SFTP | `null` (the stream is seekable, so `Stream.Length` works, at one extra round trip) | `null` |

### Move, copy, and bulk results

`MoveAsync(source, destination)` copies, then deletes the source.

- It returns `false` without touching either blob when the destination exists. The check is not atomic except on Redis: a destination created between the check and the copy can still be overwritten. Serialize moves to one key yourself when you need a hard guarantee.
- If the source delete fails after the copy, the move deletes the destination copy, best effort, so the original survives.
- A move onto itself returns `true` and does nothing.

`CopyAsync(source, destination)` keeps the source and overwrites the destination.

A `false` from either call means the source was not found (or, for `MoveAsync`, that the destination exists). A destination container that was never created is different: FileSystem and SFTP throw `DirectoryNotFoundException`, while AWS, Azure, and R2 return `false`, because their single 404 cannot tell a missing source from a missing container.

A failed or cancelled `UploadAsync` leaves the earlier blob and its metadata unchanged on every provider. On FileSystem and SFTP, `CopyAsync` gives the destination the same protection, and a concurrent reader sees the old content or the new, never a partial file.

`BulkUploadAsync(container, requests, ct)` and `BulkDeleteAsync(container, paths, ct)` return `IReadOnlyList<BlobBulkResult>`, one per entry. Each carries the raw `Container` and `Path`, a `Location` (`null` when the path was invalid), and a `Result<bool, Exception>`:

- Upload: `Ok(true)` on success, `Fail(ex)` on failure.
- Delete: `Ok(true)` deleted, `Ok(false)` not found, `Fail(ex)` on failure.

Match results by `Container` and `Path`, not by position. One failed entry does not stop the batch. `DeleteAllAsync(BlobQuery)` returns a count only when every delete succeeds. Otherwise it tries every matched blob, then throws one `AggregateException` with the failures.

A non-seekable upload stream is handled per provider: AWS and R2 stream it as a multipart upload, Redis buffers it in memory under its size limit, and Azure, FileSystem, and SFTP stream it through. A seekable stream is rewound to position 0 before the upload.

### Tenant scoping

Blob paths carry no tenant, so two tenants that each have a user `1` collide on `avatars/1.png`. `ScopeByTenant()` (`Headless.Blobs.MultiTenancy`) rewrites every location, query, bulk path, and presign request with the ambient tenant, and strips the tenant from every returned key. Application code keeps using logical locations.

- `TenantBlobScopingStrategy.PathPrefix` (default) makes the tenant id the first key segment inside your container.
- `ContainerPerTenant` gives each tenant a container named `{ContainerPrefix}{tenantId}` and makes your container the first key segment.

`IBlobContainerManager` is not scoped. A scoped store implements `IScopedBlobStorage`. Its `Unscoped` property is the wrapped store, which takes physical locations. Code that holds physical locations unwraps it: `storage is IScopedBlobStorage s ? s.Unscoped : storage`. Host-level blobs and the accepted tenant id shapes are in [Tenant-Scoped Blobs and Caches](multi-tenancy/blobs-and-caches.md#tenant-scoped-blobs-and-caches).

## Choosing a Provider

Pick one provider per store, default or named. The same provider can back several named stores, each with its own options and client.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Blobs.FileSystem` | Local development, tests, or single-node on-premises | More than one node serves the app | No shared storage; sidecar metadata; emulated paging |
| `Headless.Blobs.Aws` | Amazon S3 | Egress cost matters | Tied to S3 pricing and the AWS SDK |
| `Headless.Blobs.Aws` via `UseS3Compatible` | MinIO, Ceph RGW, Garage, or another self-hosted S3-compatible server | The server is R2 (use `Headless.Blobs.CloudflareR2`), or it needs SDK settings the helper fixes | Path-style addressing, no ACL, and a buffered signed upload body are fixed; use `UseAws` with `AWSOptions` for anything else |
| `Headless.Blobs.CloudflareR2` | S3-compatible storage with low egress cost and private buckets | You need public ACLs, or bucket creation from the app | No ACLs; no `IBlobContainerManager`, so create buckets outside the app |
| `Headless.Blobs.Azure` | Azure, with Entra ID auth and SAS URLs | Not on Azure | Needs a `BlobServiceClient`; SAS from an Entra ID client needs an extra role |
| `Headless.Blobs.SshNet` | Files must land on an SFTP server | High throughput, or native presigned URLs | Slower; sidecar metadata costs a second round trip; holds live SSH connections |
| `Headless.Blobs.Redis` | Small, short-lived blobs such as thumbnails or temporary uploads | Large or durable files | Memory cost; 10 MB default limit; unordered paging |

## Write a blob storage provider

Implement `IBlobStorage` yourself when no shipped provider fits your backend. Callers, tenant scoping, `UseSignedUrlEndpoint`, and `DecorateHeadlessBlobs` then work with your store unchanged, as long as it keeps the contract below. Reference `Headless.Blobs` (registration) and `Headless.Blobs.Abstractions` (contracts and helpers). The provider-author helpers (`BlobLocationResolver`, `BlobStorageHelpers`, and `PathValidation`) are public types in the `Headless.Blobs` namespace, so the `using Headless.Blobs;` your store already has covers them.

### Resolve every address through BlobLocationResolver

`BlobLocation` and `BlobQuery` validate path security at construction, but they do not normalize. Normalization is two-tier and belongs to your provider:

- **Container: strict.** `IBlobNamingNormalizer.NormalizeContainerName` applies your backend's container rules (case, length, allowed characters).
- **Path: lenient.** `IBlobNamingNormalizer.NormalizeBlobName` runs once per `/`-delimited key segment, applying your backend's object-name rules.

Never read `location.Container` or `location.Path` into a backend call directly. Turn every address into a backend address through `BlobLocationResolver`:

| Call | Use for | Returns |
| --- | --- | --- |
| `BlobLocationResolver.Resolve(location, normalizer)` | Every operation on one blob | `(string Container, string Key)` |
| `BlobLocationResolver.ResolveQuery(query, normalizer)` | `ListAsync` and `DeleteAllAsync` | `(string Container, string? Prefix)` |
| `BlobLocationResolver.ResolveContainer(container, normalizer)` | `IBlobContainerManager` methods, which take a raw container name | `string` |

Normalizers are lossy, so a valid input can normalize into a dangerous form (`.*.` into `..`, `x.hlmet:a` into `x.hlmeta`, a non-empty prefix into nothing). The resolver re-validates the normalized result and throws `ArgumentException` for an empty container or segment, a `.` or `..` segment, a reserved sidecar segment, a container that resolves to the storage root, or a non-empty prefix that normalizes to empty. A prefix never silently widens into a whole-container match.

`CrossOsNamingNormalizer` strips the characters a file name cannot hold on any operating system (`\ / : * ? " < > |` and the rest of `FileNames.InvalidFileNameChars`), from containers and segments alike. The FileSystem, Redis, and SFTP providers use it. Implement `IBlobNamingNormalizer` yourself when your backend has stricter rules.

Give each store its own normalizer instance, and pass the same kind to its `IBlobContainerManager`, so a container name resolves to the same backend name for data and for lifecycle calls.

### Keep the data-plane contract

Every rule in [Core Concepts](#core-concepts) is part of the contract. These are the ones a provider enforces, with the public helper that implements each:

| Obligation | Helper |
| --- | --- |
| Rewind a seekable upload stream to position 0. Stream a non-seekable one, or buffer it under a stated limit. | — |
| Never create a missing top-level container in `UploadAsync`; throw. Report `RequiresContainerProvisioning` honestly: `true` unless the first write creates the container. | — |
| Leave the earlier blob and its metadata unchanged when an upload fails or is cancelled. | `BlobStorageHelpers.BuildTempUploadPath(path)` for write-then-rename backends |
| When `contentType` is `null`, derive it from the key's extension. | `IMimeTypeProvider.GetMimeType(path)` (`Headless.IO`) |
| Store the caller's metadata plus the framework keys `uploadDate` and `extension`. Return only the caller's keys. Fill `BlobInfo.Created` from the stored upload date. | `BuildEffectiveMetadata(metadata, uploadDate, path)`, `ToUserMetadata(metadata)`, `ParseUploadDate(metadata, fallback)`; keys `UploadDateMetadataKey`, `ExtensionMetadataKey` |
| Return `null` from `OpenReadStreamAsync` and `GetBlobInfoAsync` for a missing blob. Set `BlobDownloadResult.FileName` to the full container-relative key. Fill `ContentLength` and `ContentType` when the backend returns them. | — |
| Leave `BlobInfo.Metadata` `null` in listings unless the query has `IncludeMetadata`. | — |
| Wrap the native cursor in the shared token envelope. Return a `null` token on the last page. Throw `ArgumentException` for a malformed token. | `EncodeContinuationToken(nativeCursor)`, `DecodeContinuationToken(token)` |
| Return one `BlobBulkResult` per input in bulk calls, failing one entry without aborting the batch. Rethrow only the caller's cancellation. | `RunBulkAsync(container, items, maxParallelism, pathSelector, body, ct)` |
| Return the count from `DeleteAllAsync`, or try every match and then throw one `AggregateException`. | `CountDeletedOrThrow(results, operation)` |
| `MoveAsync`: compare the resolved source and destination first, and return `true` for a move onto itself. Otherwise return `false` for an occupied destination or a missing source, copy, then delete the source. When the source delete fails and the source is still there, delete the copy and rethrow. | `MoveViaCopyThenDeleteAsync(...)` |
| Dispose backend clients in `DisposeAsync` (`IBlobStorage` is `IAsyncDisposable`). | — |

A backend with no native metadata stores it in a sidecar beside each blob, as FileSystem and SFTP do. Name it with `BlobStorageHelpers.SidecarSuffix` (`.hlmeta`), filter every key for which `IsSidecarKey(key)` is `true` out of listings, existence checks, counts, and deletes, and move or delete the sidecar with its blob. `BlobLocation` already rejects user keys that end in the suffix.

`PathValidation.ValidatePathSegment(segment)` applies the traversal, absolute-path, and control-character checks to a raw string, such as a key your backend returns.

### Add capabilities

- **Container management.** Implement `IBlobContainerManager` (`EnsureContainerAsync`, `ContainerExistsAsync`, `DeleteContainerAsync`) as a separate type and register it beside the store. Leave it unregistered when your credentials cannot manage containers; callers then get `null`.
- **Presigned URLs.** Implement `IPresignedUrlBlobStorage` on the storage type itself, so `storage is IPresignedUrlBlobStorage` holds. Report only the constraints your backend enforces in `SupportedUploadConstraints`. Honor both `PresignedDownloadOptions` members; build the `Content-Disposition` value with `BlobStorageHelpers.BuildAttachmentContentDisposition(fileName)`. Without native presigning, implement nothing: `UseSignedUrlEndpoint` wraps your store.

### Register the provider

Add `Use{Provider}` extensions to both builders, with the overload trio `IConfiguration`, `Action<TOptions>`, and `Action<TOptions, IServiceProvider>`:

- **Default store.** Call `setup.RegisterDefaultProvider(services => ...)` on `HeadlessBlobsSetupBuilder`. Inside, register an unkeyed singleton `IBlobStorage`, and an unkeyed `IBlobContainerManager` if you have one.
- **Named store.** Call `instance.RegisterProvider(services => ...)` on `HeadlessBlobInstanceBuilder`, exactly once; a second call throws `InvalidOperationException`. Register keyed singletons under `instance.Name`: `IBlobStorage`, `IBlobContainerManager` if any, and, when the store presigns, an `IPresignedUrlBlobStorage` that forwards to the keyed store. Read named options with `IOptionsMonitor<TOptions>.Get(name)`, never `CurrentValue`, because keyed DI does not pass the key to constructor dependencies.
- **Options.** Bind and validate with `services.Configure<TOptions, TValidator>(..., name)` from `Headless.Hosting`, which validates at startup.
- **Cross-cutting services** that must run after every store registers go through `setup.RegisterCrossCuttingExtension(services => ...)`.

`AddHeadlessBlobs` runs these actions only after its own checks pass.

```csharp
public static class SetupMyBlob
{
    public static HeadlessBlobsSetupBuilder UseMyBlob(this HeadlessBlobsSetupBuilder setup, Action<MyBlobOptions> configure)
    {
        setup.RegisterDefaultProvider(services =>
        {
            services.Configure<MyBlobOptions, MyBlobOptionsValidator>(configure);
            services.AddSingleton<IBlobStorage>(sp => new MyBlobStorage(
                sp.GetRequiredService<IOptions<MyBlobOptions>>().Value,
                new MyNamingNormalizer() // one normalizer per store
            ));
            services.AddSingleton<IBlobContainerManager>(sp => new MyBlobContainerManager(
                sp.GetRequiredService<IOptions<MyBlobOptions>>().Value,
                new MyNamingNormalizer()
            ));
        });

        return setup;
    }

    public static HeadlessBlobInstanceBuilder UseMyBlob(this HeadlessBlobInstanceBuilder instance, Action<MyBlobOptions> configure)
    {
        var name = instance.Name;

        instance.RegisterProvider(services =>
        {
            services.Configure<MyBlobOptions, MyBlobOptionsValidator>(configure, name);
            services.AddKeyedSingleton<IBlobStorage>(name, (sp, _) => new MyBlobStorage(
                sp.GetRequiredService<IOptionsMonitor<MyBlobOptions>>().Get(name),
                new MyNamingNormalizer()
            ));
        });

        return instance;
    }
}
```

Inside the store, resolve before every backend call:

```csharp
public async ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default)
{
    var (container, key) = BlobLocationResolver.Resolve(location, _normalizer);

    return await _client.ObjectExistsAsync(container, key, cancellationToken);
}
```

**Verify:** in a test, register the store through `AddHeadlessBlobs`, then assert: an upload to a missing container throws; an upload then `ExistsAsync` returns `true`; `OpenReadStreamAsync` returns the metadata you uploaded without `uploadDate` or `extension`; a two-page `ListAsync` round-trips the token, and a forged token throws `ArgumentException`; `MoveAsync` onto an existing blob returns `false`; and a location whose segment normalizes to empty throws `ArgumentException`. Run the same assertions against a shipped provider such as `UseFileSystem` to compare behavior.

---

## Headless.Blobs.Abstractions

The contracts: `IBlobStorage`, `BlobLocation`, `BlobQuery`, `BlobPage`, `BlobBulkResult`, `IBlobContainerManager`, `IPresignedUrlBlobStorage`, `IScopedBlobStorage`, `IBlobStorageProvider`, and the `BlobStorageExtensions` helpers (`GetBlobsAsync`, `GetBlobsListAsync`, `UploadContentAsync`, `GetBlobContentAsync`). Reference it alone from a library that leaves registration to the host. It has no registration and no options.

```csharp
public sealed class FileService(IBlobStorage storage)
{
    public async Task UploadAsync(Stream file, string fileName, string contentType, CancellationToken ct)
    {
        var location = new BlobLocation("uploads", "images", fileName); // container "uploads", key "images/<fileName>"

        await storage.UploadAsync(
            location,
            file,
            contentType: contentType,
            metadata: new Dictionary<string, string> { ["uploaded-by"] = "user-123" },
            cancellationToken: ct
        );
    }

    public async Task<string?> GetContentAsync(string fileName, CancellationToken ct)
    {
        var location = new BlobLocation("uploads", "images", fileName);

        await using var result = await storage.OpenReadStreamAsync(location, ct);

        if (result is null)
        {
            return null;
        }

        using var reader = new StreamReader(result.Stream);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task<IReadOnlyList<BlobInfo>> ListImagesAsync(CancellationToken ct)
    {
        var query = new BlobQuery("uploads", prefix: "images/");
        var blobs = new List<BlobInfo>();

        await foreach (var blob in storage.GetBlobsAsync(query, ct))
        {
            blobs.Add(blob);
        }

        return blobs;
    }
}
```

`OpenReadStreamAsync` and `GetBlobInfoAsync` return `null` for a missing blob. `ExistsAsync` and `DeleteAsync` return `bool`.

---

## Headless.Blobs

The registration entry point, `AddHeadlessBlobs(Action<HeadlessBlobsSetupBuilder>)`, and `IBlobStorageProvider`.

### Setup

```bash
dotnet add package Headless.Blobs
```

Add at least one provider package.

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store: injected as plain IBlobStorage.
    blobs.UseCloudflareR2(options =>
    {
        options.AccountId = builder.Configuration["R2:AccountId"]!;
        options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
        options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
    });

    // Named store: injected as keyed IBlobStorage("docs").
    blobs.AddNamed(
        "docs",
        instance =>
            instance.UseAzure(
                setupAction: options => { },
                clientFactory: _ => new BlobServiceClient(builder.Configuration["Azure:Docs:ConnectionString"])
            )
    );

    // Two named stores on one provider, each with its own options.
    blobs.AddNamed("scratch", instance => instance.UseFileSystem(options => options.BaseDirectoryPath = "/tmp/blobs"));
    blobs.AddNamed(
        "archive",
        instance => instance.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions("AWS:Archive"))
    );
});
```

**Verify:** invalid provider options fail the host start with `OptionsValidationException`. In an integration test, upload to a location, then assert `ExistsAsync` returns `true`.

Resolve stores:

```csharp
// Default store.
public sealed class UploadService(IBlobStorage storage) { }

// Named store, keyed.
public sealed class DocsService([FromKeyedServices("docs")] IBlobStorage docsStorage) { }

// Named store, by name.
public sealed class MultiStoreService(IBlobStorageProvider provider)
{
    public IBlobStorage GetDocs() => provider.GetStorage("docs"); // throws when not registered

    public IBlobStorage? TryGet(string name) => provider.GetStorageOrNull(name);

    public bool HasStore(string name) => provider.RegisteredNames.Contains(name);
}

// Named container management: null for an R2 store.
public sealed class ProvisioningService([FromKeyedServices("docs")] IBlobContainerManager? docsManager)
{
    public ValueTask EnsureAsync(string container, CancellationToken ct) =>
        docsManager?.EnsureContainerAsync(container, ct) ?? ValueTask.CompletedTask;
}

// Named presigned URLs: AWS, Azure, and R2, or any store behind UseSignedUrlEndpoint.
public sealed class PresignedService([FromKeyedServices("docs")] IPresignedUrlBlobStorage presigned)
{
    public Task<Uri> GetDownloadUrl(BlobLocation location) =>
        presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromHours(1)).AsTask();
}
```

### Configuration

No options of its own. Each provider's `Use{Provider}` takes an `IConfiguration` section, an `Action<TOptions>`, or an `Action<TOptions, IServiceProvider>`.

### Design and runtime behavior

- The default store is optional, and there is at most one. A configuration with only named stores is valid and leaves plain `IBlobStorage` unregistered.
- `AddHeadlessBlobs` throws before it changes the service collection for a second default, a duplicate name, or a named store with no provider.
- `IBlobStorageProvider` is a singleton. `RegisteredNames` lists only named stores, never the default. Check it before you resolve a name that came from outside.
- Every store has its own options, client, and naming rules. `IMimeTypeProvider` and `TimeProvider` are shared.
- `services.DecorateHeadlessBlobs(new BlobStorageDecoration((store, name, services) => ...))` wraps the default store and every named store after all providers register, whichever call runs first. The factory gets the store name (`null` for the default) and returns the store to hand out, or the same store to leave it alone. Decorations apply in registration order, a later one wrapping an earlier one. Registering the same instance twice adds it once. `BlobStorageDecoration.DecoratedRegistrations` counts the wrapped registrations.

---

## Headless.Blobs.MultiTenancy

Rewrites every blob location with the ambient tenant before it reaches the provider. See [Tenant scoping](#tenant-scoping).

### Setup

```bash
dotnet add package Headless.Blobs.MultiTenancy
```

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
{
    blobs.UseAws(options => { /* ... */ });
    blobs.AddNamed("shared", instance => instance.UseAws(options => { /* ... */ }));
});

builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims())
        .Blobs(blobs => blobs.ScopeByTenant(options => options.UnscopedStores.Add("shared")))
);

public sealed class AvatarService(IBlobStorage storage)
{
    // Stored as avatars:{tenantId}/{userId}.png; another tenant's {userId}.png never collides.
    public ValueTask SaveAsync(string userId, Stream image, CancellationToken ct) =>
        storage.UploadAsync(new BlobLocation("avatars", $"{userId}.png"), image, cancellationToken: ct);
}
```

**Verify:** the host fails to start with `HEADLESS_TENANCY_BLOBS_NO_SCOPED_STORE` when the scoping wrapped no store. Resolved `IBlobStorage` is `IScopedBlobStorage`. In a test, upload one location under two tenants with `currentTenant.Change(tenantId)` and assert each tenant reads its own content.

### Configuration

`TenantBlobScopingOptions`, set through `ScopeByTenant(options => ...)`:

| Option | Default | Effect |
| --- | --- | --- |
| `Strategy` | `PathPrefix` | `PathPrefix` stores `BlobLocation("avatars", "1.png")` at `avatars:{tenantId}/1.png`. `ContainerPerTenant` stores it at `{ContainerPrefix}{tenantId}:avatars/1.png`. |
| `ContainerPrefix` | `""` | Prepended to the tenant id to name a tenant's container under `ContainerPerTenant`, for example `myapp-` to keep S3 bucket names unique. Lowercase letters, digits, and hyphens. |
| `UnscopedStores` | empty | Named stores left unscoped, for blobs every tenant shares. The default store is always scoped. |

### Design and runtime behavior

- Wraps the default store and every named store registered through `AddHeadlessBlobs`, as the outermost wrapper, whichever of `AddHeadlessBlobs` and `ScopeByTenant()` runs first. Keyed `IPresignedUrlBlobStorage` resolves to the wrapper. A store registered directly as `IBlobStorage`, and every `IBlobContainerManager`, are not wrapped.
- Strips the tenant from every returned key (`BlobInfo.BlobKey`, `BlobDownloadResult.FileName`, `BlobBulkResult`). A returned key outside the tenant's scope throws `InvalidOperationException`. A bulk path is validated before the tenant is added, so an invalid path such as `""` fails its entry instead of addressing the tenant root.
- An operation with no ambient tenant throws `MissingTenantContextException` (HTTP 403, `g:tenant_required`) before the provider runs. A tenant id that is not one safe storage segment throws `InvalidOperationException`.
- Run cross-tenant work inside `currentTenant.Change(tenantId)`.
- A wrapped store implements `IPresignedUrlBlobStorage` exactly when the wrapped store does. The signed-URL endpoint and the data-protection key ring use `Unscoped`, because they hold physical locations.
- Registers an `AsyncLocal` `ICurrentTenant` fallback when no tenant source is registered, and records the `Blobs` seam as `Guarded` with the `scope-by-tenant` capability.

---

## Headless.Blobs.Aws

Amazon S3, and S3-compatible servers through `UseS3Compatible`.

### Setup

```bash
dotnet add package Headless.Blobs.Aws
```

```csharp
// Default store; the AWS SDK credential and region chain applies unless awsOptions overrides it.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions())
);
```

Explicit credentials instead (one shape per app, since `AddHeadlessBlobs` runs once):

```csharp
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
```

Create the bucket before the first upload:

```csharp
var manager = serviceProvider.GetService<IBlobContainerManager>();

if (manager is not null)
{
    await manager.EnsureContainerAsync("my-bucket");
}

await storage.UploadAsync(new BlobLocation("my-bucket", "reports/q1.pdf"), stream);
```

**Verify:** invalid options fail the host start with `OptionsValidationException`. In an integration test against LocalStack or MinIO, assert `ContainerExistsAsync("my-bucket")` returns `true` after `EnsureContainerAsync`, and `ExistsAsync` returns `true` after an upload.

### MinIO and other S3-compatible servers

`UseS3Compatible` builds the client from an endpoint URL and static credentials, with the settings those servers accept:

- Path-style addressing, because self-hosted servers rarely have wildcard DNS for bucket names.
- `AuthenticationRegion` defaults to `us-east-1`, the region MinIO assumes.
- Request checksums and response validation only when an operation requires them (`WHEN_REQUIRED`), not the AWS SDK v4 default CRC checksums.
- No canned ACL, and a buffered, signed upload body without `aws-chunked` streaming. Payloads stay signed because the SDK refuses unsigned payloads over `http://`.
- A plain `http://` endpoint fails options validation at startup unless `AllowInsecureHttp = true`. Set it only for local development.

The container manager is registered as for `UseAws`, so `EnsureContainerAsync` creates buckets. Presigned URLs keep the endpoint's scheme, so a local `http://` MinIO returns `http://` URLs.

```csharp
// Local MinIO with its default root credentials.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseS3Compatible(
        "http://localhost:9000",
        "minioadmin",
        "minioadmin",
        options => options.AllowInsecureHttp = true
    )
);
```

Bind from configuration, with a named store on a second endpoint:

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
{
    blobs.UseS3Compatible(builder.Configuration.GetSection("Minio"));
    blobs.AddNamed("archive", instance => instance.UseS3Compatible(builder.Configuration.GetSection("MinioArchive")));
});
```

```json
{
  "Minio": {
    "ServiceUrl": "https://minio.internal:9000",
    "AccessKeyId": "your-access-key",
    "SecretAccessKey": "your-secret-key",
    "AuthenticationRegion": "us-east-1",
    "AllowInsecureHttp": false,
    "MaxBulkParallelism": 10
  }
}
```

`ServiceUrl`, `AccessKeyId`, and `SecretAccessKey` are required, and `ServiceUrl` must be an absolute `http` or `https` URL. When a server needs a setting the helper fixes, use `UseAws(options => { ... }, awsOptions)` and configure the SDK yourself.

**Verify:** a missing required value, or a plain `http://` endpoint without `AllowInsecureHttp`, fails the host start with `OptionsValidationException`. Against the server, `EnsureContainerAsync("my-bucket")` followed by `ContainerExistsAsync("my-bucket")` returns `true`.

### Configuration

`AwsBlobStorageOptions` (`UseAws`):

| Option | Default | Notes |
| --- | --- | --- |
| `CannedAcl` | `S3CannedACL.Private` | ACL applied to uploaded objects. |
| `UseChunkEncoding` | `true` | `aws-chunked` upload bodies. |
| `DisablePayloadSigning` | `false` | Skips payload signing. |
| `MaxBulkParallelism` | `10` | Parallel requests in bulk operations and metadata listings. |

`S3CompatibleBlobStorageOptions` (`UseS3Compatible`): `ServiceUrl`, `AccessKeyId`, `SecretAccessKey`, `AuthenticationRegion` (default `us-east-1`), `AllowInsecureHttp` (default `false`), and `MaxBulkParallelism` (default `10`).

`awsOptions` (`AWSOptions` from `AWSSDK.Extensions.NETCore.Setup`) overrides the SDK's credential and region chain. Each store builds its own `IAmazonS3`; none is registered in DI.

### Design and runtime behavior

- **Registrations.** The default store registers unkeyed `IBlobStorage` and `IBlobContainerManager`. A named store registers keyed `IBlobStorage`, `IPresignedUrlBlobStorage`, and `IBlobContainerManager`. `UseS3Compatible` registers the same services.
- **Uploads.** A stream up to 5 MiB is one `PutObject`. A larger or non-seekable stream is a multipart upload in 5 MiB parts, holding two part buffers at a time whatever the content size. A failed or cancelled multipart upload is aborted, so no incomplete parts stay billed. A multipart object's `ETag` ends in `-N` (the part count) and is not an MD5 of the content.
- **Metadata.** `GetBlobInfoAsync` reads metadata from the HEAD response. In listings, `Created` is the object's `LastModified`.
- **Presigned URLs.** An upload `ContentType` constraint is signed into the URL. `MaxLength` is ignored, because a presigned PUT cannot bound its size. `PresignedDownloadOptions` are signed in as `response-content-disposition` and `response-content-type`.
- **Containers.** `EnsureContainerAsync` remembers the buckets it ensured for the life of the store.

---

## Headless.Blobs.Azure

Azure Blob Storage.

### Setup

```bash
dotnet add package Headless.Blobs.Azure
```

```csharp
// The BlobServiceClient the default store uses when it has no clientFactory.
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]));

builder.Services.AddHeadlessBlobs(blobs =>
{
    blobs.UseAzure(options => { });

    // Named store on another account: clientFactory replaces the DI client.
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

Without a `clientFactory`, register a `BlobServiceClient` in DI. A missing client fails when the store is first resolved, not at startup.

**Verify:** the readiness health check `blobs-azure` (default store) or `blobs-azure-{name}` (named store), tagged `ready`, `headless`, and `blobs`, reports `Healthy`; `dotnet MyApp.dll check` runs it before a rollout ([Health checks](utilities.md#health-checks)). Invalid options fail the host start with `OptionsValidationException`.

### Configuration

| `AzureStorageOptions` | Default | Notes |
| --- | --- | --- |
| `ContainerPublicAccessType` | `PublicAccessType.None` | Access type `EnsureContainerAsync` applies when it creates a container. |
| `CacheControl` | `max-age=7776000, must-revalidate` | `Cache-Control` set on uploaded blobs (90 days). |
| `MaxBulkParallelism` | `10` | Parallel requests in bulk uploads. |
| `HealthCheckContainerName` | `null` | When set, the health check reads this container's properties instead of listing containers. |

### Design and runtime behavior

- **Registrations.** The default store registers unkeyed `IBlobStorage` and `IBlobContainerManager`. A named store registers keyed `IBlobStorage`, `IPresignedUrlBlobStorage`, and `IBlobContainerManager`.
- **Health check permissions.** By default the probe lists one page of the account's containers, so the identity needs `containers/read` (Storage Blob Data Reader or above, or an account SAS that can list). With `HealthCheckContainerName`, a container-scoped reader role is enough, and the check also proves the container exists.
- **Presigned URLs.** SAS URLs. An account-key client signs locally. An Entra ID client (`DefaultAzureCredential`) uses a user-delegation SAS, which needs the `Storage Blob Delegator` role and is capped at 7 days. A client built from a bare SAS token or with anonymous access throws `InvalidOperationException` when you mint a URL. A SAS cannot constrain an upload, so `SupportedUploadConstraints` is `None`. `PresignedDownloadOptions` are signed in as the SAS `rscd` and `rsct` fields.
- **Bulk delete** uses the Azure Batch API.
- **Uploads.** A non-seekable stream passes through without buffering.
- **Containers.** `EnsureContainerAsync` remembers the containers it ensured for the life of the store.

---

## Headless.Blobs.CloudflareR2

Cloudflare R2 as a private S3-compatible store, on the AWS provider's engine.

### Setup

```bash
dotnet add package Headless.Blobs.CloudflareR2
```

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
{
    blobs.UseCloudflareR2(options =>
    {
        options.AccountId = builder.Configuration["R2:AccountId"]!;
        options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
        options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
        // options.Jurisdiction = R2Jurisdiction.EuropeanUnion; // optional
    });

    // Named store: keyed IPresignedUrlBlobStorage("media") is registered too.
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

Or bind a section with `blobs.UseCloudflareR2(builder.Configuration.GetSection("R2"))`:

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

Create buckets in the Cloudflare dashboard or with infrastructure as code. The app cannot create them.

**Verify:** invalid options fail the host start with `OptionsValidationException`. After you create the bucket, upload a test blob and assert `ExistsAsync` returns `true`; an upload to a missing bucket throws.

### Configuration

| `R2BlobStorageOptions` | Default | Notes |
| --- | --- | --- |
| `AccountId` | required | Cloudflare account id. |
| `AccessKeyId` | required | R2 API token access key. |
| `SecretAccessKey` | required | R2 API token secret. |
| `Jurisdiction` | `R2Jurisdiction.Default` | Selects the endpoint: default, EU, or FedRAMP. |
| `EndpointUrl` | `null` | Overrides the endpoint. `{0}` is replaced with `AccountId`. Takes precedence over `Jurisdiction`. |

The provider always uses path-style addressing, the `auto` region, the checksum settings R2 accepts, no ACL, no chunked encoding, and unsigned payloads.

### Design and runtime behavior

- **Registrations.** The default store registers unkeyed `IBlobStorage`. A named store registers keyed `IBlobStorage` and `IPresignedUrlBlobStorage`. No `IBlobContainerManager` is registered, so `RequiresContainerProvisioning` is `true` and the bucket must exist before the first upload.
- **No public access.** R2 has no ACLs. Serve private content through presigned URLs. Public serving (custom domains, `r2.dev`) is out of scope.
- **Presigned URLs** behave as on AWS: `ContentType` is enforced and `MaxLength` is ignored. R2 honors the `response-content-*` overrides `PresignedDownloadOptions` signs in.
- **Single upload size.** One PUT is capped at about 5 GiB, as on S3.

---

## Headless.Blobs.FileSystem

Blobs as files under a base directory, for development, tests, and single-node on-premises hosts.

### Setup

```bash
dotnet add package Headless.Blobs.FileSystem
```

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseFileSystem(options =>
        options.BaseDirectoryPath = Path.Combine(builder.Environment.ContentRootPath, "storage")
    )
);
```

Or bind a section with `blobs.UseFileSystem(builder.Configuration.GetSection("FileSystemBlob"))`:

```json
{
  "FileSystemBlob": {
    "BaseDirectoryPath": "/var/data/blobs"
  }
}
```

**Verify:** after `EnsureContainerAsync("uploads")`, the directory `{BaseDirectoryPath}/uploads` exists. In a test, upload a blob and assert `ExistsAsync` returns `true`.

### Configuration

| `FileSystemBlobStorageOptions` | Default | Notes |
| --- | --- | --- |
| `BaseDirectoryPath` | required | Root directory. Each container is a directory under it. |

### Design and runtime behavior

- **Registrations.** Unkeyed `IBlobStorage` and `IBlobContainerManager` for the default store, keyed for a named one. `IPresignedUrlBlobStorage` only through `UseSignedUrlEndpoint`.
- **Containers.** `EnsureContainerAsync` creates the container directory. `UploadAsync` creates the intermediate directories of a path, but a missing container directory throws.
- **Metadata** lives in `.hlmeta` sidecar files ([Metadata and sidecar companions](#metadata-and-sidecar-companions)).
- **Paging** is the emulated re-scan tier ([Listing](#listing-token-paging-and-guarantee-tiers)).
- **Atomic replace.** `UploadAsync` and `CopyAsync` write the content and the sidecar to temporary sibling files, then rename each over its final path once the body is complete. A failed or cancelled write leaves the earlier blob and metadata in place and deletes its temporaries. A reader that opened the old file keeps reading it. A process crash during an upload can leave a temporary file, hidden from listings, until the container is deleted. A crash between the two renames pairs the new content with the old metadata.
- **Uploads.** A non-seekable stream is written straight to disk.

---

## Headless.Blobs.Redis

Small, short-lived blobs in Redis hashes.

### Setup

```bash
dotnet add package Headless.Blobs.Redis
```

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("localhost:6379"));

// ConnectionMultiplexer is left unset, so the store uses the registered IConnectionMultiplexer.
builder.Services.AddHeadlessBlobs(blobs => blobs.UseRedis(options => options.MaxBlobSizeBytes = 5 * 1024 * 1024));
```

**Verify:** the host fails to start with `OptionsValidationException` when neither `ConnectionMultiplexer` nor a registered `IConnectionMultiplexer` exists. In a test, upload a blob and assert `ExistsAsync` returns `true`.

### Configuration

| `RedisBlobStorageOptions` | Default | Notes |
| --- | --- | --- |
| `ConnectionMultiplexer` | The registered `IConnectionMultiplexer` | Set it to give the store its own connection. Configuration binding cannot set it, so `UseRedis(IConfiguration)` always uses the registered one. |
| `MaxBlobSizeBytes` | 10 MB | Largest blob. `0` disables the limit. |
| `MaxBulkParallelism` | `10` | Parallel operations in bulk calls. |

### Design and runtime behavior

- **Registrations.** Unkeyed `IBlobStorage` and `IBlobContainerManager` for the default store, keyed for a named one. Also adds `TimeProvider`, `IJsonOptionsProvider`, and `IJsonSerializer` when they are not registered. `IPresignedUrlBlobStorage` only through `UseSignedUrlEndpoint`.
- **Size limit.** An upload over `MaxBlobSizeBytes` is rejected. A non-seekable stream is buffered in memory under the same limit. Use S3 or Azure for large files.
- **No real container.** `RequiresContainerProvisioning` is `false`. `EnsureContainerAsync` does nothing, `ContainerExistsAsync` is `true` when any key exists under the container, and `DeleteContainerAsync` deletes the container's keys.
- **Metadata** lives in a separate info hash, written atomically with the content.
- **Paging** is the cursor-scan tier: order is not by key, and a blob can appear twice. A token that decodes to a non-numeric cursor throws `ArgumentException`.
- **Redis Cluster.** A container maps to the hashes `{container}/` and `blob-info/{container}/`. The braces are a hash tag, so both share one cluster slot, and every upload, delete, and same-container move or copy runs as one atomic script. A container name with `{` or `}` throws `ArgumentException`. A move or copy between containers in different slots is copy-then-delete, with the same best-effort rollback as other providers.
- **Retries.** A connection or timeout failure is retried up to 3 times, with a jittered back-off from 50 ms to 500 ms on the system clock.
- **Calls that do not retry after sending.** `MoveAsync`, `DeleteAsync` (and each delete in `BulkDeleteAsync` and `DeleteAllAsync`), and the destination write of a cross-slot move retry only a command the client never sent. After a send, they throw the `RedisTimeoutException` or `RedisConnectionException`, because a re-run cannot tell whether the first run took effect. Check the blob before you act on the failure. Uploads, reads, listings, and copies keep the full retry.

---

## Headless.Blobs.SshNet

Blobs as files on an SFTP server.

### Setup

```bash
dotnet add package Headless.Blobs.SshNet
```

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options => options.ConnectionString = "sftp://user:password@sftp.example.com:22/home/user/uploads")
);
```

Or bind a section with `blobs.UseSsh(builder.Configuration.GetSection("SftpBlob"))`:

```json
{
  "SftpBlob": {
    "ConnectionString": "sftp://user:password@sftp.example.com:22/home/user/uploads"
  }
}
```

SSH key authentication:

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options =>
    {
        options.ConnectionString = "sftp://user@sftp.example.com:22/home/user/uploads";
        options.PrivateKey = System.IO.File.OpenRead("/path/to/key");
        options.PrivateKeyPassPhrase = "optional-passphrase"; // nullable
    })
);
```

The store reads the `PrivateKey` stream once, when it creates its connection pool, and disposes it. Every connection then uses the parsed key.

**Verify:** a key that cannot be parsed fails when the store is first resolved, so resolve `IBlobStorage` once after `builder.Build()` to fail at startup. In an integration test, upload a blob and assert `ExistsAsync` returns `true`.

### Configuration

| `SshBlobStorageOptions` | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | `sftp://user[:password]@host:port/root/path`. |
| `PrivateKey` | `null` | Key stream for key authentication. |
| `PrivateKeyPassPhrase` | `null` | Passphrase of `PrivateKey`. |
| `AllowNoneAuthentication` | `false` | When `false`, a connection string with no password and no `PrivateKey` throws. Set `true` only for intentional passwordless login. |
| `Proxy` | `null` | Proxy URI (`http://user:pass@host:port` or a SOCKS URI). |
| `ProxyType` | `ProxyTypes.None` | SSH.NET proxy type. Detected from the `Proxy` scheme when left at `None`. |
| `MaxConcurrentOperations` | `4` | Parallel operations in bulk calls. Keep it low; SFTP channels are limited. |
| `MaxPoolSize` | `4` | Pooled connections for uploads, deletes, listings, and info reads. At least `MaxConcurrentOperations`. |
| `MaxReadStreamConnections` | `4` | Connections held by open download streams. |

A store can open up to `MaxPoolSize + MaxReadStreamConnections` SSH connections.

### Design and runtime behavior

- **Registrations.** Unkeyed `IBlobStorage` and `IBlobContainerManager` for the default store, keyed for a named one. Each store has its own connection pool. `IPresignedUrlBlobStorage` only through `UseSignedUrlEndpoint`.
- **Containers.** `EnsureContainerAsync` creates the directory, like `mkdir -p`.
- **Metadata** lives in `.hlmeta` sidecar files, which cost a second round trip on every write and read that carries metadata.
- **Paging** is the emulated re-scan tier.
- **A directory is not a blob.** A key that names a directory reads as missing: `ExistsAsync` returns `false`, `GetBlobInfoAsync` returns `null`, and `DeleteAsync` returns `false` (`Ok(false)` in `BulkDeleteAsync`) and leaves the directory in place.
- **Atomic replace.** `UploadAsync` and `CopyAsync` write to temporary sibling files and rename them over the final path with OpenSSH's `posix-rename@openssh.com` extension, so a failed write leaves the earlier blob in place. On a server without that extension, the earlier blob is first renamed aside, then deleted after the new file takes its path, or renamed back on failure. A partial write is never visible, but the blob is briefly absent during the replace.
- **Download streams hold a connection.** `OpenReadStreamAsync` keeps its SFTP connection until you dispose the `BlobDownloadResult`, so a slow client holds it for the whole transfer. These connections come from the separate `MaxReadStreamConnections` set, so open downloads never block other operations.
- **No acquire timeout.** A full pool waits until a connection frees or the operation's `CancellationToken` cancels. After `MaxReadStreamConnections` undisposed downloads, every later `OpenReadStreamAsync` waits. Dispose download results promptly, size both pools for peak concurrency, and always pass a cancellation token.
- **Uploads.** A non-seekable stream passes through unbuffered.

---

## Headless.Blobs.SignedUrlEndpoint

Signed download and upload URLs for stores with no native presigning (FileSystem, Redis, SFTP), served by an endpoint in your app. Use it to keep one presigned-URL code path from FileSystem in development to S3 or Azure in production, or when production runs on one of these providers.

### Setup

```bash
dotnet add package Headless.Blobs.SignedUrlEndpoint
```

```csharp
builder.Services.AddHeadlessBlobs(setup =>
{
    setup.UseFileSystem(options => options.BaseDirectoryPath = "/var/app/blobs");
    setup.AddNamed("exports", instance => instance.UseRedis(options => options.ConnectionMultiplexer = redis));
    setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("https://api.example.com"));
});

var app = builder.Build();
app.MapBlobSignedUrlEndpoint(); // GET and PUT at /blobs/{token}

// The calling code is the same for every provider:
if (storage is IPresignedUrlBlobStorage presigned)
{
    var download = await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(15), cancellationToken: ct);
    var upload = await presigned.GetPresignedUploadUrlAsync(
        location,
        TimeSpan.FromMinutes(15),
        new PresignedUploadConstraints { ContentType = "application/pdf", MaxLength = 10 * 1024 * 1024 },
        ct
    );
}
```

Call both `UseSignedUrlEndpoint` (services) and `MapBlobSignedUrlEndpoint` (routes). Mapping without the registration throws `InvalidOperationException`.

**Verify:** the default store resolves as `IPresignedUrlBlobStorage`. A `GET` on a minted download URL returns `200` with the blob, and the same URL with one character changed returns `404`. Each request logs one `SignedUrlAccess` entry.

### Configuration

`BlobSignedUrlOptions`, validated at startup:

| Option | Default | Meaning |
| --- | --- | --- |
| `BaseUrl` | none, required | Public absolute `http` or `https` URL of the app, with any path base and no query. Minted URLs start with it. It is configured, not read from the request, because URLs are often minted in background work and a reverse proxy can rewrite the host. |
| `RoutePrefix` | `/blobs` | Path the endpoint maps under and minted URLs use. |
| `LogBlobLocation` | `false` | Adds the blob's container and path to the access log entry. Off because a key often contains a user or tenant id. |

### Design and runtime behavior

- **Which stores it wraps.** After every provider registers, each default and named store that does not implement `IPresignedUrlBlobStorage` is wrapped so that it does. AWS, Azure, and R2 stores keep their native presigning. Named stores also get keyed `IPresignedUrlBlobStorage`. A wrapped default store is no longer its concrete provider type, and a store you registered as an instance is now disposed by the container.
- **Token.** The URL is `{BaseUrl}{RoutePrefix}/{token}`. The token is an ASP.NET Core Data Protection payload that authenticates the store name, container, path, verb, expiry, upload constraints, and download options, so a URL cannot be reused for another blob, store, or verb. Expiry is checked against the registered `TimeProvider`. A token is reusable until it expires and cannot be revoked one at a time.
- **404 for every refusal.** An expired, tampered, wrong-verb, or unknown-store token, and a missing blob, all return `404`, never `403`, so a probe learns nothing.
- **Download.** `GET` streams the blob, with range support when the stream is seekable. The response type is derived from the key's extension, or from `PresignedDownloadOptions.ContentType`. Responses carry `Content-Disposition: attachment` (named after the last path segment, or `PresignedDownloadOptions.FileName`), `X-Content-Type-Options: nosniff`, and `Content-Security-Policy: sandbox`, because an uploaded HTML or SVG file would otherwise run script in your app's origin. A browser downloads the file instead of rendering it; an `<img>` tag still works. For untrusted uploads, prefer a separate host name for `BaseUrl`.
- **Upload.** `PUT` writes the body through `UploadAsync` and returns `200`.
  - A `ContentType` constraint must name one exact media type (`image/*` throws `ArgumentException` when minting). Another type gets `415`; parameters such as `charset` are ignored.
  - A `MaxLength` constraint requires `Content-Length` and no `Transfer-Encoding` (`411` otherwise), answers a larger declared length with `413` before any byte reaches the store, and sets the server's request-body limit to that length where the server allows it. Without `MaxLength`, the server's default body limit applies.
  - A missing container is a server error, as for `UploadAsync`.
  - A failed upload (disconnect, over-limit body, store refusal) leaves the blob already at that location unchanged, and a new key absent. Kestrel answers an over-limit body with its own `413`.
- **Access.** Both routes allow anonymous access, because the token is the credential, and are excluded from OpenAPI. `MapBlobSignedUrlEndpoint` returns the `RouteGroupBuilder`, so attach rate limiting, CORS, or request-timeout policies to it.
- **Key ring.** Every replica that mints or serves URLs must share one persisted Data Protection key ring, or a URL minted on one replica returns `404` on another. `Headless.DataProtection.Blobs` can persist it to blob storage. Revoking or deleting the key that protected a URL breaks it. Default key rotation keeps retired keys, so rotation alone does not.
- **Trade-off.** Every byte streams through your app and counts against its bandwidth, memory, and request limits. The value is one code path from development to production, not offloading.

**Access log.** After checking the token, the endpoint logs one `Information` entry per request (category `Headless.Blobs.SignedUrlEndpoint`, event `SignedUrlAccess`) with the operation (`Download`, `Upload`), the outcome (`accepted`, `expired`, `wrong-operation`, `unreadable`, `store-not-found`), the grant's expiry when the token was readable, and `TokenFingerprint`: the first 12 hex characters of the token's SHA-256. The fingerprint cannot be reversed and is the same on every replica. To match an entry to the code that minted a URL, hash the URL's last segment the same way. With `LogBlobLocation`, the event is `SignedUrlAccessWithLocation` and also names `{Container}/{Path}`.

**The token stays out of logs and traces.** Anyone who reads a logged path could use the URL until it expires, so `UseSignedUrlEndpoint` always rewrites request paths under `RoutePrefix` (or under `BaseUrl`'s path plus `RoutePrefix`, before `UsePathBase` runs) to `{RoutePrefix}/***`, for every method. There is no option to turn it off.

- **ASP.NET Core request logs.** `UseSignedUrlEndpoint` registers `AddHeadlessRedaction` (`Headless.Logging.Redaction`) and an `ILogPathRedactor` for the route. They rewrite the `Microsoft.AspNetCore.Hosting.Diagnostics` category: the request scope's `RequestPath` (carried by every log entry of the request, including the `{RequestPath}` in the Headless Serilog console template) and the `Request starting` and `Request finished` lines. Other categories are untouched. `AddHeadlessSerilog` keeps the redaction. A logger factory registered later some other way, such as a plain `UseSerilog`, replaces it, and the host logs `LogRedactionFactoryReplaced` (Warning) once at startup. Then call `builder.Services.AddHeadlessRedaction()` after that registration, or set `Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics` to `None`.
- **HTTP logging and server spans.** `AddHeadlessRequestLogRedaction` (`Headless.Logging.Redaction.AspNetCore`) rewrites the `Path` field of `UseHttpLogging`, and `url.path`, `http.target`, and `url.full` on the request's server activity (and on `Activity.Current` when the OpenTelemetry instrumentation created a separate one), from a middleware first in the pipeline. Exporters receive `***`. A sampler, or a processor's `OnStart`, runs before the middleware and still sees the token.
- **Other spans**, such as an outgoing `HttpClient` call to a signed URL, are rewritten by `AddHeadlessLogPathRedaction()` on the tracer provider. `builder.AddHeadless()` from `Headless.Api.ServiceDefaults` adds it with its OpenTelemetry setup. A host that builds its own tracer provider adds it before the exporter.

These still carry the token, outside the framework's reach:

- W3C logging (`UseW3CLogging`): leave `W3CLoggingFields.UriStem` out of its fields.
- Serilog request logging (`UseSerilogRequestLogging`): resolve `BlobSignedUrlPathRedactor`, and in its `EnrichDiagnosticContext` callback replace `RequestPath` with the result of `TryRedact(httpContext.Request.Path, out var redacted)`.
- The `Microsoft.AspNetCore.Hosting` EventSource `RequestStart` event (seen by `dotnet-trace` or an `EventListener`), and the logs of a reverse proxy or load balancer in front of the app.
