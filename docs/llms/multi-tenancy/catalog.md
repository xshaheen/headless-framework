---
domain: Multi-Tenancy
packages: MultiTenancy.Storage.EntityFramework
---

# Tenant catalog

> Part of [Multi-Tenancy](../multi-tenancy.md). Store tenants, read tenant metadata through `ICurrentTenantInfo`, choose a store, and keep the catalog cache correct when tenants change. Resolving a tenant from a host name, route, or header is in [Identifier resolution](identifier-resolution.md).

## Catalog concepts

- **Tenant identifier and canonical tenant id.** A tenant identifier is the public name used to find a tenant: a host label (`acme` from `acme.example.com`), a route value, or a header value. It can change over the tenant's life (a rebrand, a custom domain). The catalog trims and lowercases it (invariant) before any lookup. Never key data or authorization by it. The canonical tenant id is the stable id that keys everything tenant-owned: `ICurrentTenant.Id`, EF filters and guards, job and message propagation, and per-tenant Settings, Features, and Permissions. A token's tenant claim carries the canonical id, so claim resolution never touches the catalog.
- **The catalog holds identity, routing, and lifecycle only.** `TenantInfo` carries `Id`, `Identifier`, `Name`, `IsEnabled`, and `ExtraProperties`. Keep per-tenant configuration (feature flags, limits, branding) in Settings, Features, or Permissions, keyed by the canonical id. They already give you a cached, managed per-tenant store.
- **Extend the tenant shape in three steps**, from least to most structural:
    1. **`ExtraProperties`** holds JSON-serializable metadata. Lookups and cache keys never use it, and the EF store saves it as one serialized column. The in-memory store and catalog cache snapshot nested values as detached `JsonElement` instances. Each read gets its own metadata dictionary and can replace entries locally.
    2. **Subclass `TenantInfo`** in your own `ITenantStore`, with typed, queryable columns. A value that needs an index is either the tenant identifier or a column in your own store, never an `ExtraProperties` entry.
    3. **Typed accessor**: `services.AddTypedCurrentTenantInfo<T>(projection)` registers `ICurrentTenantInfo<T>`. The projection builds `T` from the base `TenantInfo` and may re-read your own store. When the store already returned a `T`, the accessor uses it directly.
- **The store contract is read-only.** `ITenantStore` has two members, `FindByIdentifierAsync` and `FindByIdAsync`. `ITenantDirectory.GetAllAsync()` adds enumeration. The catalog normalizes, shape-validates, and caches. A store receives an already normalized identifier and must not normalize again. Implementing `ITenantStore` over your own tenant aggregate is a first-class path. The shipped stores are conveniences, not a required schema.
- **Accessor reads.** `ICurrentTenantInfo.GetAsync()` reads `ICurrentTenant.Id` on every call, with no per-scope memo. Inside a nested `ICurrentTenant.Change(...)` it returns the inner tenant, and after the scope ends, the outer one. It never throws for a missing tenant: `null` means no ambient tenant, no store configured, or no row for the id. A disabled tenant still reads, with `IsEnabled = false`. Reads never reject anything, so never use one as an authorization check. Disabled tenants are rejected only by identifier resolution. When `ICurrentTenant.Name` and the catalog name differ, for example in a scope restored by a job or message, trust the catalog value.
- **Staleness.** A resolved identifier-to-id mapping and an id-to-`TenantInfo` entry stay cached for `TenantCatalogOptions.CacheExpiration` (default 5 minutes). A disable or metadata change reaches readers within that window. `UnknownIdentifierCacheExpiration` (default 30 seconds) is the shorter window for a cached "unknown" answer: a new tenant becomes resolvable within it. The store contract is read-only, so the catalog never sees your writes. Evict changed entries early with `ITenantCatalogCacheInvalidator` ([Cache invalidation](#cache-invalidation)). A disabled tenant can stay active for `max(claim lifetime, CacheExpiration)`: a claim-resolved request never consults the catalog, so a disabled tenant's still-valid token keeps passing `TenantRequirement` until it expires. Identifier resolution is bounded by `CacheExpiration`.
- **Never reuse an identifier quickly.** Evicting a re-pointed identifier fixes every node that reads the shared cache, but not all of them: a lookup that read the store before your write can write the old mapping back after the eviction, and a hybrid-cache peer whose shared copy had expired keeps its local copy until that expires. Retire an identifier for at least `CacheExpiration` before you point it at a different tenant.

## Choosing a Tenant Catalog Store

`.Catalog(...)` needs exactly one store. Zero or two stores fail startup. It also needs a caching provider ([Tenant Catalog](#tenant-catalog)).

| Store | Use when | Avoid when | Trade-off |
|---|---|---|---|
| **In-memory** (`UseInMemory(...)`, in `Headless.MultiTenancy`) | Tests, small apps, a fixed tenant list in code. | Tenants change at run time or must change without a redeploy. | An immutable snapshot built at startup from seed `TenantInfo` entries. No external dependency. |
| **Configuration** (`UseConfiguration(...)`, in `Headless.MultiTenancy`) | Operators manage the tenant list in configuration and a restart to apply changes is fine. | End users or an admin UI create or edit tenants at run time. | Bound once at startup. A configuration change needs a process restart: there is no reload on change. |
| **Entity Framework Core** (`UseEntityFramework<TContext>()`, in `Headless.MultiTenancy.Storage.EntityFramework`) | Application code creates, edits, or onboards tenants at run time, and you already use EF Core. | A static or operator-managed list is enough. | You own the `Tenants` table and its migrations. Headless only reads. Your own `DbContext` inserts and updates. |

When none fits (a richer aggregate, another persistence technology, a remote directory service), implement `ITenantStore` yourself.

## Tenant Catalog

The catalog is off by default. Without `.Catalog(...)`, `ICurrentTenantInfo.GetAsync()` always returns `null` and claim resolution is unaffected. The catalog serves two independent needs:

1. **Metadata access.** `.Catalog(...)` alone lets `ICurrentTenantInfo` return the `TenantInfo` of whatever tenant is ambient, usually from claim resolution. This posture (`catalog-accessor`) is valid on its own.
2. **Identifier resolution.** `.Http(http => http.ResolveFromCatalog(...))` with `app.UseHeadlessTenantCatalogResolution()` also resolves the ambient tenant from a public identifier before authentication (`catalog-resolution`). See [Identifier resolution](identifier-resolution.md).

Both need **a caching provider**. `Headless.MultiTenancy` references only `Headless.Caching.Abstractions`, and the `ICache<T>` implementation comes from `Headless.Caching.InMemory`, `.Redis`, or `.Hybrid`. Call `AddHeadlessCaching(...)` in any host that calls `.Catalog(...)`, including a metadata-only host. Without it, startup fails in every environment with `MissingRequiredServiceException`, which names the remedy. When Settings, Permissions, or Features also lack a cache, one exception lists every feature.

### Accessor-only setup

```csharp
builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());

builder.AddHeadlessTenancy(tenancy =>
{
    tenancy
        .Http(http => http.ResolveFromClaims()) // existing claim-based resolution, unchanged
        .Catalog(catalog =>
            catalog.UseInMemory(options =>
            {
                options.Tenants.Add(new TenantInfo(id: "ten_123", identifier: "acme", name: "Acme Inc", isEnabled: true));
            })
        );
});
```

```csharp
public sealed class ProfileService(ICurrentTenantInfo tenantInfo)
{
    public async Task<string?> GetTenantDisplayNameAsync(CancellationToken cancellationToken) =>
        (await tenantInfo.GetAsync(cancellationToken))?.Name;
}
```

The pipeline does not change. The claim middleware sets the ambient id, and `ICurrentTenantInfo` looks it up.

**Verify:** in a test, inside `currentTenant.Change("ten_123")`, `tenantInfo.GetAsync(ct)` returns a `TenantInfo` whose `Name` is `"Acme Inc"`. Remove `AddHeadlessCaching(...)` and the host fails to start with `MissingRequiredServiceException`.

### Configuration

`TenantCatalogOptions`, set with `.Catalog(catalog => catalog.Configure(options => ...))` and validated at startup:

| Option | Default | Effect |
|---|---|---|
| `CacheExpiration` | 5 minutes | How long a resolved identifier-to-id mapping and an id-to-`TenantInfo` entry stay cached. |
| `UnknownIdentifierCacheExpiration` | 30 seconds | How long an "unknown identifier" answer stays cached. `TimeSpan.Zero` caches no unknown answer, and concurrent lookups of one identifier then each read the store. |
| `IgnoredIdentifiers` | empty | Identifiers that resolve to host context with no store call, such as `www`. The only ignore list: identifier sources have none. |
| `MaxIdentifierLength` | 63 | Longest accepted identifier after normalization. Startup rejects values outside 1 to `TenantCatalogOptions.MaxIdentifierLengthLimit` (253). |
| `IdentifierPattern` | DNS-label slug | The shape every identifier must match. A custom pattern must carry a match timeout: startup rejects `Regex.InfiniteMatchTimeout`. |
| `DetailedResolutionErrors` | `false` | Returns distinct rejection codes instead of one generic 404. Development and trusted environments only. See [Failure mapping](identifier-resolution.md#failure-mapping). |

`MaxIdentifierLength` and `IdentifierPattern` apply to every identifier from every source. Widening them for custom domains widens them for subdomains too. See [Whole-host identifiers](identifier-resolution.md#whole-host-custom-domain-identifiers).

### Catalog behavior

- `.Catalog(...)` registers `TenantCatalogOptions`, the store, the catalog service `ITenantCatalogService` (scoped), the cache invalidator, and the catalog-backed `ICurrentTenantInfo`.
- **In-memory store.** `UseInMemory` has three overloads: `Action<InMemoryTenantStoreOptions>`, `Action<InMemoryTenantStoreOptions, IServiceProvider>`, and an `InMemoryTenantStoreOptions` instance. There is no `IConfiguration` overload, because `TenantInfo` has no parameterless constructor for the binder. Startup rejects two seeds with the same normalized identifier or the same id. Building the store snapshots the seed metadata and rejects values that cannot be encoded as JSON.
- **Configuration store.** `UseConfiguration` takes an `IConfiguration` section, an `Action<ConfigurationTenantStoreOptions>`, or an `Action<ConfigurationTenantStoreOptions, IServiceProvider>`. The section holds a `Tenants` array whose entries have `Id`, `Identifier`, `Name`, `IsEnabled` (default `true`), and `ExtraProperties` (string values). Startup rejects a blank `Id` or `Identifier`, duplicate ids, duplicate normalized identifiers, and an identifier that does not match the default shape (DNS label, at most 63 characters). That check uses the default shape even when you widened `TenantCatalogOptions`.
- **Store faults propagate.** An exception from your `ITenantStore` reaches the caller unwrapped, usually as a 500. It never maps to a tenant code. A cache read or write fault counts as a cache miss, and the store answer stands. If the catalog cannot snapshot a cached tenant's metadata, it reloads from the store. If it cannot snapshot the store's metadata for caching, it returns the original store result and skips the metadata cache write.
- **A wrong store answer is a fault.** When a store returns a tenant whose `Identifier` differs from the queried normalized identifier, or whose `Id` differs from the queried id, the catalog throws `InvalidOperationException` and caches nothing, so a faulty store cannot route one tenant's identifier to another.
- **Cache shape.** One store hit from an identifier lookup fills both the identifier and the id cache. The cache always holds the base `TenantInfo` shape. A subclass from your store is copied down before caching, and the typed accessor rebuilds `T` on read. Concurrent lookups of one identifier after an expiry cost one store read per process. An id with no catalog row is never cached, so a tenant created later is found on the next read.

## Cache invalidation

`ITenantCatalogCacheInvalidator`, a singleton that `.Catalog(...)` registers, evicts catalog entries after you change a tenant in the store. Each method removes one exact key, so a wildcard character that a custom `IdentifierPattern` allows cannot widen an eviction. Neither method looks up related entries, because this node may not hold an entry a peer still serves. Name every key the change touched:

| Store change | Calls |
|---|---|
| Identifier re-pointed to a different tenant | `InvalidateIdentifierAsync(identifier)` |
| Tenant disabled, enabled, or metadata changed | `InvalidateTenantAsync(id)` |
| Identifier renamed | `InvalidateTenantAsync(id)`, `InvalidateIdentifierAsync(oldIdentifier)`, `InvalidateIdentifierAsync(newIdentifier)` |
| Tenant created under an identifier probed while unknown | `InvalidateIdentifierAsync(identifier)` |

```csharp
await db.SaveChangesAsync(cancellationToken); // commit the store change first

await invalidator.InvalidateTenantAsync(tenant.Id, cancellationToken);
await invalidator.InvalidateIdentifierAsync(oldIdentifier, cancellationToken);
await invalidator.InvalidateIdentifierAsync(tenant.Identifier, cancellationToken);
```

- Invalidate after the store write commits.
- `InvalidateIdentifierAsync` trims and lowercases its argument the same way resolution does.
- A cache fault during eviction propagates to your code, so a failed eviction is never silent.
- Eviction is not a hard cut-over. A lookup that read the store before your write can write the old answer back, for a re-point, a disable, or an edit. On a hybrid cache, peers drop their local copy only when the shared cache held the key, and a failed broadcast is logged (or queued for replay under auto-recovery), not thrown, so a peer can serve its local copy until it expires. Every such window ends within the configured expirations, which is why the [no-reuse rule](#catalog-concepts) still applies.
- When `RequireTenantOnPublish()` is on, the hybrid-cache invalidation messages still reach peers: Headless marks them as system messages.

**Verify:** in a test, read a tenant through `ICurrentTenantInfo`, change its `Name` in the store, call `InvalidateTenantAsync(id)`, and assert the next read returns the new name.

## Migration Guidance

A host that resolves tenants only from claims can add a catalog later with no behavior change:

1. **Skip the catalog until you need it.** Nothing breaks without it.
2. **Add metadata access first.** Call `.Catalog(catalog => catalog.UseInMemory(...))`, `UseConfiguration(...)`, or `UseEntityFramework<TContext>()` with no `ResolveFromCatalog(...)`. `ICurrentTenantInfo` starts returning data for claim-resolved tenants, and nothing else changes.
3. **Add identifier resolution when you need it** (custom domains, a subdomain per tenant, a tenant lookup before sign-in). Follow [Identifier-based resolution setup](identifier-resolution.md#identifier-based-resolution-setup).
4. **Mismatch enforcement turns on with `ResolveFromCatalog(...)`.** It applies only to requests that carry both an identifier and a tenant claim. Claim-only hosts see no change.
5. **Swap stores freely.** Moving from `UseInMemory` to `UseEntityFramework<TContext>()` changes nothing else: the catalog service, caching, and HTTP wiring stay the same.

---

## Headless.MultiTenancy.Storage.EntityFramework

The EF Core catalog store, read from your own `DbContext`.

### Setup

```bash
dotnet add package Headless.MultiTenancy.Storage.EntityFramework
```

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureHeadlessTenancyCatalog(this);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
);

builder.Services.AddHeadlessCaching(caching => caching.UseInMemory()); // the catalog's hard prerequisite

builder.AddHeadlessTenancy(tenancy =>
{
    tenancy.Catalog(catalog => catalog.UseEntityFramework<AppDbContext>());
});
```

Add a migration for the `TenantRecord` table, and insert tenants through your own `DbContext`.

**Verify:** the host starts. When `OnModelCreating` misses `ConfigureHeadlessTenancyCatalog(this)`, startup throws `InvalidOperationException` naming the `DbContext` and that call. A missing factory fails with `MissingRequiredServiceException`, and a scoped or transient factory with `InvalidServiceLifetimeException`.

### Configuration

None. Cache and identifier-shape settings live on `TenantCatalogOptions` ([Configuration](#configuration)).

### Design and runtime behavior

- `ConfigureHeadlessTenancyCatalog(this)` maps `TenantRecord` (`Id`, `Identifier`, `NormalizedIdentifier`, `Name`, `IsEnabled`, `ExtraProperties`) to one table with a unique index on `NormalizedIdentifier`.
- The unique index uses a case- and accent-sensitive collation (`Latin1_General_100_BIN2` on SQL Server, `C` on PostgreSQL, `BINARY` on SQLite), so a lookup never matches a row that differs only by case. Other providers (MySQL, Oracle, third-party) keep their default collation, which is often case-insensitive. Pin a binary collation yourself after the catalog call: `modelBuilder.ConfigureHeadlessTenancyCatalog(this); modelBuilder.Entity<TenantRecord>().Property(x => x.NormalizedIdentifier).UseCollation("utf8mb4_bin");` (`utf8mb4_bin` on MySQL, `BINARY` on Oracle).
- Set the identifier with `TenantRecord.SetIdentifier(...)`. It derives `NormalizedIdentifier`, which has no public setter, so a seeded or renamed row never carries a stale normalized value.
- `Identifier` and `NormalizedIdentifier` are `TenantRecordConstants.IdentifierMaxLength` (253) characters wide, so a whole-host custom-domain identifier fits.
- `TenantRecord` does not implement `IMultiTenant`: the catalog sits outside the EF tenant query filter.
- The store only reads (`FindByIdentifierAsync`, `FindByIdAsync`, `GetAllAsync`), with `AsNoTracking()`. After you write a `TenantRecord`, evict the changed entries ([Cache invalidation](#cache-invalidation)).
- The store is a singleton that reads through `IDbContextFactory<TContext>`, so the factory must be a singleton too. `AddDbContextFactory<TContext>()` and `AddPooledDbContextFactory<TContext>()` (plain `DbContext`), and `AddHeadlessDbContext<TContext>()` and `AddHeadlessDbContextPool<TContext>()` (`HeadlessDbContext`), all register one by default.
