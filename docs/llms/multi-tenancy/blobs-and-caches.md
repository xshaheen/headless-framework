---
domain: Multi-Tenancy
packages:
---

# Tenant-scoped blobs and caches

> Part of [Multi-Tenancy](../multi-tenancy.md). Keep files and cache entries apart per tenant, and know which cache scopes are shared.

## Tenant-Scoped Blobs and Caches

The query filter and write guard keep rows apart, but a file or a cache entry has no tenant column. Two tenants that each have a user `1` collide on the cache key `user:1` and the blob path `avatars/1.png`. Both seams below put the tenant into the key or path. Both are opt-in and refuse an operation that has no tenant.

### Blob storage

`Headless.Blobs.MultiTenancy` owns the setup, the options table, and the wrapper behavior: see [Headless.Blobs.MultiTenancy](../blobs.md#headlessblobsmultitenancy).

```csharp
builder.Services.AddHeadlessBlobs(blobs => blobs.UseAws(options => { /* ... */ }));

builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(blobs => blobs.ScopeByTenant()));
```

Application code keeps addressing `new BlobLocation("avatars", "1.png")`. Pick a strategy:

| `TenantBlobScopingStrategy` | Physical location of `BlobLocation("avatars", "1.png")` under tenant `acme` | Use when |
| --- | --- | --- |
| `PathPrefix` (default) | container `avatars`, key `acme/1.png` | One set of containers serves every tenant. Delete one tenant's files with `DeleteAllAsync(new BlobQuery("avatars"))` under that tenant. |
| `ContainerPerTenant` | container `{ContainerPrefix}acme`, key `avatars/1.png` | You need per-tenant bucket policy or lifecycle, or offboarding by deleting one container. |

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Blobs(blobs =>
        blobs.ScopeByTenant(options =>
        {
            options.Strategy = TenantBlobScopingStrategy.ContainerPerTenant;
            options.ContainerPrefix = "myapp-"; // S3 bucket names are global across accounts
            options.UnscopedStores.Add("shared"); // a named store every tenant shares
        })
    )
);
```

- **No tenant, no operation.** A scoped operation with no ambient tenant throws `MissingTenantContextException` (HTTP 403 `g:tenant_required`) before it reaches the provider.
- **A tenant id must be one safe storage segment.** Under `PathPrefix`, it starts and ends with a lowercase ASCII letter or digit and contains only lowercase ASCII letters, digits, `.`, `_`, and `-`. Under `ContainerPerTenant`, `ContainerPrefix` plus the tenant id is 3 to 63 characters of lowercase ASCII letters, digits, and single hyphens, and a container used as a key segment cannot contain `/` or `\`. Any other id throws `InvalidOperationException`, because providers lowercase, strip, and truncate names and would otherwise place one tenant inside another's scope. Uppercase is refused, not folded: a FileSystem store on a case-insensitive disk (macOS, Windows) treats `Acme` and `acme` as one folder. Lowercase GUIDs (`Guid.ToString()`) and DNS-slug ids pass. Lowercase any other id before it becomes the ambient tenant.
- **Shared blobs.** Keep blobs every tenant shares in a named store listed in `UnscopedStores`, which is never wrapped. Run cross-tenant work, such as a cleanup job, once per tenant inside `currentTenant.Change(tenantId)`. A scoped store implements `IScopedBlobStorage`, whose `Unscoped` property is the wrapped store that takes physical keys (`acme/1.png`). Use it only for code that holds physical locations. Under `PathPrefix`, do not write host blobs at the top level of a container that tenants use: a host folder named like a tenant id would sit inside that tenant's prefix.
- **Data-protection key ring.** `PersistKeysToBlobStorage()` from `Headless.DataProtection.Blobs` reads and writes through `IScopedBlobStorage.Unscoped`. On a scoped store, the key ring stays at the root of its container (`BlobStorageDataProtectionOptions.ContainerName`, default `DataProtection`), shared by every tenant, whichever request first uses data protection. No tenant's `{container}/{tenantId}/` prefix reaches it.
- **Presigned URLs** are minted for the tenant's physical location. The `Headless.Blobs.SignedUrlEndpoint` endpoint serves a grant through `IScopedBlobStorage.Unscoped`, because the grant already holds the physical location and its signature is the authorization. An anonymous request can redeem a tenant's URL.
- **Not scoped.** `IBlobContainerManager` is never wrapped. Under `ContainerPerTenant`, create each tenant's container (`{ContainerPrefix}{tenantId}`) at onboarding with `EnsureContainerAsync`. A store registered directly as `IBlobStorage`, outside `AddHeadlessBlobs`, is not wrapped either. When the seam wraps no store at all, startup fails with `HEADLESS_TENANCY_BLOBS_NO_SCOPED_STORE`.

**Verify:** in a test, upload `BlobLocation("avatars", "1.png")` under `currentTenant.Change("acme")` and under `Change("globex")`, and assert each tenant reads its own content. With no tenant, the upload throws `MissingTenantContextException`.

### Application caches

```csharp
builder.Services.AddHeadlessCaching(caching => caching.UseRedis(options => { /* ... */ }));
builder.Services.AddTenantScopedCache<UserProfile>();

public sealed class ProfileReader(ICache<UserProfile> cache)
{
    // Stored as t:{tenantId}:user:1 in the underlying cache.
    public ValueTask<CacheValue<UserProfile>> GetAsync(string userId, CancellationToken ct) =>
        cache.GetAsync($"user:{userId}", ct);
}
```

`AddTenantScopedCache<T>()` (in `Headless.MultiTenancy`) registers `ICache<T>` over the default `ICache` with the key scope `t:{ICurrentTenant.Id}`, the layout the permission grant cache uses.

- Call it before or after `AddHeadlessCaching`. A second call for the same type does nothing. A call for a type that already has its own `ICache<T>` registration throws.
- The host must register an `ICurrentTenant`, as HTTP tenancy and `AddHeadlessDbContextServices()` do. Otherwise startup fails with `MissingRequiredServiceException`.
- **No tenant, no operation.** With no ambient tenant, an operation throws `MissingTenantContextException`. Keep values every tenant shares in the unscoped `ICache`. A tenant id containing `:` is refused, because `t:a:b:x` could be tenant `a` with key `b:x` or tenant `a:b` with key `x`.
- **Per-tenant eviction.** Under a tenant, `RemoveByPrefixAsync("")` evicts every entry that tenant has in the underlying cache, including other tenant-scoped types and permission grants, which share the `t:{tenantId}:` scope. Give each type its own key prefix (`profile:`) and pass that prefix to narrow the eviction. To evict another tenant's entries from host code, run the call inside `ICurrentTenant.Change(tenantId)`.
- **Not tenant-isolated.** `RemoveByTagAsync`, `ClearAsync`, and `FlushAsync` act on the whole underlying cache, and `ICache<T>.Events` does nothing. See `ScopedCache<T>` in [Caching](../caching.md).
- Settings and Features key their caches by provider name and key, not by the ambient tenant.

**Verify:** in a test, set `user:1` under `Change("acme")` and assert the underlying `ICache` holds `t:acme:user:1`. With no tenant, `cache.GetAsync("user:1", ct)` throws `MissingTenantContextException`.

## Permissions and Caching

`Headless.Permissions` scopes permission grant cache entries by tenant with no setup, using the scope `t:{ICurrentTenant.Id}`. With no tenant, the scope is `t:`, shared by every host-level operation. Once `ICurrentTenant.Id` is set, entries are isolated per tenant. Permissions keeps its own registration instead of `AddTenantScopedCache<T>()`, because it serves the host scope rather than refusing an operation with no tenant.
