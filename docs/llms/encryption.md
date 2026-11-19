---
domain: Encryption
packages: Encryption.Abstractions, Encryption, Encryption.AzureKeyVault, Encryption.EntityFramework, Encryption.PostgreSql, Encryption.SqlServer
---

# Encryption

> Field-level envelope encryption for personal data. Each value is AES-256-GCM ciphertext under a per-tenant data key. The data keys are stored wrapped by a key-encryption key (KEK) that never leaves its key service or host. Deleting a tenant's keys destroys its data (crypto-shredding).

## Orientation

Register once, with exactly one key store and exactly one key-encryption key:

```csharp
builder.Services.AddPostgreSqlSql(connectionString); // the keys live beside the data by default
builder.Services.AddHeadlessEncryption(setup =>
{
    setup.UsePostgreSql(); // or setup.UseSqlServer()
    setup.UseLocalKey(builder.Configuration.GetSection("Headless:Encryption:LocalKey"));
});
```

Then call `IFieldEncryptor` at the service boundary. The entity stores the ciphertext frame, never the plaintext:

```csharp
public sealed class Beneficiary
{
    public Guid Id { get; init; }
    public EncryptedValue NationalId { get; set; } // bytea or varbinary(max), mapped by Headless.Encryption.EntityFramework
}

var context = new FieldContext("Beneficiary.NationalId"); // scope = the current tenant
beneficiary.NationalId = await encryptor.EncryptAsync(nationalId, context, ct);

var decryption = await encryptor.DecryptAsync(beneficiary.NationalId, context, ct);
string plaintext = decryption.GetString();
```

**Verify:** run `dotnet app.dll resources provision`, then `dotnet app.dll resources status -k EncryptionKeys` shows a version for `data` and `lookup`. After start, the `warmup` readiness check turns `Healthy` once the `KeyEncryptionKey` warm-up unwraps the host key. A test that encrypts and then decrypts with the same `FieldContext` gets the plaintext back.

Packages:

- **`Headless.Encryption.Abstractions`**: `IFieldEncryptor`, `FieldContext`, `EncryptedValue`, `FieldDecryption`, `FieldDecryptionException`, `IBlindIndexer`, `BlindIndexContext`, `BlindIndex`, `IDataKeyManager`, and the provider interfaces `IKeyEncryptionKeyProvider` and `IDataKeyStore`.
- **`Headless.Encryption`**: `AddHeadlessEncryption`, the cipher, the blind indexer, the in-process key cache, `IDataKeyManager`, the `EncryptionKeys` resource, the KEK warm-up, and the local KEK (`UseLocalKey`).
- **`Headless.Encryption.AzureKeyVault`**: a KEK held in Azure Key Vault (`UseAzureKeyVault`).
- **`Headless.Encryption.EntityFramework`**: maps `EncryptedValue` properties to binary columns (`configurationBuilder.AddEncryptedValueConversions()`) and never decrypts.
- **`Headless.Encryption.PostgreSql`**, **`Headless.Encryption.SqlServer`**: the wrapped-key tables.

`Headless.Settings` stores the values of encrypted setting definitions through `IFieldEncryptor`: a tenant's values under that tenant's key, everything else under the host key. See [Encrypted settings](settings.md#encrypted-settings).

## Agent Rules

- Encrypt and decrypt explicitly through `IFieldEncryptor`. No EF Core value converter decrypts. A decrypting converter would run synchronously against an async key unwrap, would see only the ambient tenant, would put plaintext in the change tracker and audit capture, and would make equality queries silently match nothing.
- Never query an encrypted column for equality. Each encryption of one value gives different bytes. To find rows by a value, store a blind index from `IBlindIndexer` in its own column next to the ciphertext. See [Blind indexes](#blind-indexes).
- Decrypt with the same `FieldContext` the value was encrypted with: the same `Purpose`, scope, and `Binding`. The frame does not store the context, which stops a value from moving between columns, tenants, or rows. A different context throws `FieldDecryptionException` with `Failure = AuthenticationFailed`.
- Keep `Purpose` stable. It selects the column's subkey, so renaming it makes every stored value unreadable. Use the entity and property name (`Beneficiary.NationalId`).
- A `FieldContext` with no `Scope` uses `ICurrentTenant.Id`. With no current tenant it throws `MissingTenantContextException`. It never falls back to the host key, because a value under the host key would survive the tenant's shredding. For host-level data, use `FieldContext.Host(purpose)`.
- Use a tenant id or a per-subject scope (`{tenant}/{beneficiaryId}`) of at most 200 characters. Scopes compare ordinally. A scope with leading or trailing white space, or a NUL character, is refused.
- With EF Core, declare the column as `EncryptedValue` (or `EncryptedValue?` when optional) and call `configurationBuilder.AddEncryptedValueConversions()` in `ConfigureConventions`. Without EF Core, store `EncryptedValue.ToArray()` in a `bytea` or `varbinary(max)` column, or `ToBase64Url()` in a text column. Read it back with `EncryptedValue.FromBytes` or `EncryptedValue.Parse`. Both throw `FormatException` for bytes that are not a frame.
- Treat `FieldDecryptionException` with `Failure = KeyNotFound` as "this tenant's data was shredded", not as corruption.
- A shred is final. Encrypting for a shredded scope throws `DataKeyScopeShreddedException` and creates no key. To reopen the scope, call `IDataKeyManager.EnsureAsync`. It creates new keys, and the values from before the shred stay unreadable.
- To switch key-encryption-key providers (for example from `UseLocalKey` to `UseAzureKeyVault`), keep the old one registered with `KeepPreviousKeyEncryptionKey` until `RewrapAsync` returns 0. Removing it earlier leaves every key it wrapped unreadable.
- Keep KEK material out of the database and out of source control. With `UseLocalKey`, whoever reads the configured key reads every value. Prefer a key service in production when one is available.
- Run `resources provision` (or `deploy`) on every release. It creates the key tables and the host-scope keys. A starting host only verifies them.

## Core Concepts

### Key hierarchy

```text
KEK (key service, HSM, or a local 32-byte key; never stored by Headless)
 └─ wraps → data key per (scope, kind, version): 32 random bytes, stored wrapped
      ├─ kind Data   → per-purpose subkey → AES-256-GCM per value
      └─ kind Lookup → per-purpose subkey → HMAC-SHA256 per blind index
```

A data key is random and stored, not derived, so deleting its row destroys it. Each purpose (column) gets its own subkey under the data key.

### Scopes, tenants, and binding

| `FieldContext` | Encrypts under | Use for |
| --- | --- | --- |
| `new FieldContext(purpose)` | the current tenant's key | tenant data in a request |
| `new FieldContext(purpose) { Scope = tenantId }` | that tenant's key | jobs and admin work outside a tenant context |
| `FieldContext.Host(purpose)` | the host key (scope `""`) | data that belongs to no tenant |
| `… { Binding = rowIdBytes }` | the same key, also bound to the row | columns where two rows of one tenant must not swap values |

`Binding` is off by default, because an id the database generates is unknown when the value is encrypted.

`AddHeadlessEncryption` registers an AsyncLocal-backed `ICurrentTenant` unless the host registered its own, so `ICurrentTenant.Change(tenantId)` scopes encryption.

### Blind indexes

A blind index is a deterministic keyed hash of a value, stored next to its `EncryptedValue`. It lets the application find rows by exact match without storing or querying the plaintext. The hash key is the scope's Lookup data key, so the database, its indexes, and its query logs see only hashes. A tenant shred destroys the tenant's hash key with its data key.

```csharp
private static readonly BlindIndexContext _NationalIdIndex = new("Beneficiary.NationalId");

// Write: encrypt the value and hash it under the current tenant's keys.
var nationalId = NationalIds.Normalize(input); // the app's own normalization
beneficiary.NationalId = await encryptor.EncryptAsync(nationalId, new FieldContext("Beneficiary.NationalId"), ct);
var index = await blindIndexer.ComputeAsync(_NationalIdIndex, nationalId, ct);
beneficiary.NationalIdHash = index.Hash; // bytea or varbinary(32), indexed
beneficiary.NationalIdHashVersion = index.KeyVersion; // smallint, not indexed

// Query: one candidate per stored lookup-key version, newest first.
var candidates = await blindIndexer.ComputeCandidatesAsync(_NationalIdIndex, nationalId, ct);
var hashes = candidates.Select(c => c.Hash).ToList();
var match = await db.Beneficiaries.Where(b => hashes.Contains(b.NationalIdHash)).FirstOrDefaultAsync(ct);
```

| `BlindIndexContext` | Hashes under | Same value in two tenants |
| --- | --- | --- |
| `new BlindIndexContext(purpose)` | the current tenant's Lookup key. Throws `MissingTenantContextException` with no tenant | different hashes |
| `new BlindIndexContext(purpose) { Scope = tenantId }` | that tenant's Lookup key | different hashes |
| `BlindIndexContext.Host(purpose)` | the host Lookup key (scope `""`) | the same hash, for deliberate cross-tenant matching, such as one person registered by two tenants |
| `… { Length = 16 }` | the same key, hash truncated to 8–32 bytes (default 32) | — |

- **Normalize first.** The hash covers the exact UTF-8 bytes, so two spellings of one value (spaces, digit scripts, case) hash differently. Normalize and validate before hashing, with the same normalization for writes and queries.
- **One context per column.** Keep each column's `BlindIndexContext` in one place. Writes and queries that disagree on purpose, scope, or length match nothing. Renaming the purpose makes every stored hash unmatchable.
- **Hash length.** A shorter hash leaks less about how often a value repeats, and matches more unrelated rows. Below 32 bytes, confirm every match by decrypting the value.
- **Input text.** Every `IBlindIndexer` method encodes the value as strict UTF-8 and throws `ArgumentException` on a lone surrogate.
- **Writes.** `ComputeAsync` hashes under the scope's active Lookup key and creates it on first use, like encryption. On a shredded scope it throws `DataKeyScopeShreddedException`.
- **Queries.** `ComputeCandidatesAsync` hashes under every stored version, newest first, and never creates a key. A scope with no Lookup key, or a shredded scope, returns no candidates, so the query matches nothing. It reads the scope's version list from the store on every call, so a rotation, retirement, or shred on another host shows at once.
- **Rotation.** `RotateAsync(scope, DataKeyKind.Lookup)` adds a version. `ComputeAsync` uses it once each host's cached active key expires. Meanwhile `ComputeCandidatesAsync` returns both versions, so no row stops matching. A rehash job decrypts each row's value and calls `RehashAsync(context, value, storedKeyVersion)`. It returns the hash to store under the active version, or `null` when the row is already current. It also returns `null` when the stored version is above this host's cached active version, so a host that has not seen the rotation never moves a row back. When the job has finished and `KeyCacheDuration` has passed since the rotation, call `RetireAsync(scope, DataKeyKind.Lookup, oldVersion)`. Queries then stop hashing a candidate for that version. `RetireAsync` refuses Data keys and the active version.
- **Shredding.** A blind index cannot be recomputed after its tenant is shredded, but the hashes left in the rows still link that tenant's rows to each other. Clear the hash columns when you erase the tenant's data. Host-scope hashes are unaffected by a tenant shred.
- **Not supported.** Prefix, partial, and range matching. Automatic hashing in an EF Core converter: compute the hash explicitly next to `EncryptAsync`.
- **Logging.** The lookup key, the derived hash key, and the input value are never logged or put in an exception message.

### The frame

`EncryptedValue` is one binary frame, 46 bytes longer than the plaintext:

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 1 | format version, `0x01` |
| 1 | 1 | algorithm, `0x01` = AES-256-GCM under a per-purpose subkey |
| 2 | 16 | data-key id (big-endian UUID) |
| 18 | 12 | random nonce |
| 30 | n | ciphertext |
| 30+n | 16 | GCM tag |

Decryption looks up the key by the id in the frame. It never tries keys in turn. The header, scope, purpose, and binding are authenticated, so a changed header byte or a moved value fails the tag.

### Key lifecycle

- **Creation.** The first encryption for a scope with no data key creates version 1, with one KEK wrap. Two hosts racing on a new tenant agree on one key. To fail onboarding early when the KEK is unreachable, call `IDataKeyManager.EnsureAsync(tenantId)`: it creates the tenant's Data and Lookup keys up front.
- **Rotation.** `RotateAsync(scope, DataKeyKind.Data)` adds the next version, which encrypts every new value. Older versions keep decrypting. `FieldDecryption.KeyIsCurrent` is `false` for a value under an older version, so a re-encryption job decrypts and encrypts it again. Other hosts switch to the new version when their cached active key expires.
- **KEK rotation.** Make a new KEK version current (for the local key: add a key and point `ActiveKeyId` at it), then run `RewrapAsync()`. It re-wraps every data key the current KEK version did not wrap and returns how many it changed. No encrypted value changes. Keep the old KEK version enabled until a run returns 0.
- **Switching providers.** Register the new provider with its `Use…` member and keep the old one with `setup.KeepPreviousKeyEncryptionKey(previous => previous.UseLocalKey(...))`. The new provider wraps new data keys. The provider named in each key's row unwraps old keys. `RewrapAsync()` moves every old key to the new provider. Once it returns 0, remove the previous provider. A previous provider must have a different name from the active one. To rotate keys within one provider, use its own key versions.
- **Shredding.** `ShredAsync(scope)` deletes every key of the scope, marks the scope shredded, and returns how many keys it deleted. This host forgets them at once. Another host forgets them when its cached copy expires (`EncryptionOptions.KeyCacheDuration`, 1 hour by default). After that, every value of the scope fails with `KeyNotFound`.
- **Retiring a lookup version.** `RetireAsync(scope, DataKeyKind.Lookup, version)` deletes an old blind-index key version after the rehash job. See [Blind indexes](#blind-indexes). Data keys never retire, because a value needs its key for as long as it is stored.
- **A shred is final.** A later encryption for the scope throws `DataKeyScopeShreddedException`, and so does `RotateAsync`. Nothing creates a key for the scope implicitly. An encryption racing the shred on any host either commits its key first, which the shred then deletes, or is refused. `EnsureAsync(scope)` is the only way back: it clears the shredded marker and creates new keys. The values from before the shred stay unreadable.

**Backup limit.** A database backup taken before a shred still holds the wrapped keys, and restoring it while the shared KEK exists recovers the data. Shredding is immediate on live systems and complete once backup retention passes. A guarantee against backups needs a per-tenant KEK deleted in the key service. The stored KEK id per key allows that without a schema change.

A per-tenant KEK adds only that backup guarantee. It does not shred faster, because key services delay deletion on purpose. AWS KMS waits 7 to 30 days (30 by default) before deleting a key. Azure Key Vault keeps a deleted key recoverable for 7 to 90 days (90 by default), with soft-delete that cannot be turned off and, under purge protection, no early purge. Deleting the data-key rows is faster: immediate on this host, and on every host within `KeyCacheDuration`.

### Deploy and startup

| What | Created by | A starting host |
| --- | --- | --- |
| The KEK | the operator or infrastructure-as-code. Headless never creates one | does nothing |
| The key tables | `resources provision` (kind `Schema`) | fails the start when they are missing, naming `resources provision` |
| The host-scope keys | `resources provision -k EncryptionKeys` | makes one read. Until it passes, readiness reports the resource missing |
| Tenant keys | `EnsureAsync` during onboarding, or the first encryption | does nothing |
| KEK reachability | — | the `KeyEncryptionKey` warm-up unwraps the host Data key once after start. The `warmup` readiness check stays `Unhealthy` until it succeeds. The start never waits on the key service |

The `EncryptionKeys` resource refuses `resources purge` and `resources delete`, because removing keys destroys data. Only `ShredAsync` deletes keys. `resources status -k EncryptionKeys` prints the host scope's newest `data` and `lookup` versions, or `missing`.

### Key service load

A host calls the KEK about once per data key per `KeyCacheDuration`: an unwrap when a key is first used or its cache entry expires, and a wrap when a key is created. Concurrent misses for one key share one unwrap. Expect a burst of roughly hosts × active tenants unwraps after a deployment, when every cache starts empty, then a steady trickle as entries expire.

Size the key service for that burst. Azure Key Vault allows 2,000 RSA-2048 or 500 RSA-3072 operations per 10 seconds per vault for HSM-protected keys (twice that for software keys), but only 250 for RSA-4096. Prefer a 2048-bit or 3072-bit KEK. AWS KMS shares 10,000 to 100,000 symmetric-key requests per second per Region across operations. A longer `KeyCacheDuration` lowers the load, at the cost of slower shredding and rotation on other hosts.

### Limits

- **Values per key.** Each value gets a random 96-bit nonce, and NIST SP 800-38D allows at most 2³² such encryptions under one key. The subkey is per scope, purpose, and data-key version, so the budget is 2³² values per tenant, column, and version. Rotate the scope's Data key with `RotateAsync` well before a column approaches that. Rotation starts a fresh budget.
- **No key commitment.** AES-GCM is not key-committing. The scheme does not need it, because the frame names its key inside the authenticated data, and data keys are random keys the host generates, never keys a caller chooses.

### Threat model

It protects against:

- a leaked database dump or backup while the KEK stays outside the database;
- read-only SQL injection that exfiltrates rows;
- casual database or support access;
- ciphertext copied across tenants or columns;
- tampering.

It does not protect against:

- a compromised application host, which holds the KEK permission and the key cache;
- an attacker with the application's KMS identity;
- an injection that returns decrypted data;
- same-tenant, same-column row swaps unless `Binding` is set;
- someone with database write access corrupting key rows.

### Testing

Use `UseLocalKey` with a fixed test key in tests. Cached keys expire on the registered `TimeProvider`, so a test can advance a fake clock past `KeyCacheDuration` to see another host's shred or rotation take effect.

## Choosing a Provider

| Key store | Use when | Trade-off |
| --- | --- | --- |
| `Headless.Encryption.PostgreSql` | The application's data is in PostgreSQL | Keys back up with the data |
| `Headless.Encryption.SqlServer` | The application's data is in SQL Server | Keys back up with the data |

| Key-encryption key | Use when | Trade-off |
| --- | --- | --- |
| `UseLocalKey` (in `Headless.Encryption`) | Development, tests, or a host whose secret store holds the key | The host process and its configuration hold the KEK itself |
| `UseAzureKeyVault` (`Headless.Encryption.AzureKeyVault`) | Production on Azure | One Key Vault call per data key per host per cache duration. The KEK never leaves the vault |

Keep the key tables in the application's database (the default), so tenant creation and backups stay consistent. A separate connection string is allowed.

---

## Headless.Encryption.Abstractions

The contracts that application and domain code reference.

```bash
dotnet add package Headless.Encryption.Abstractions
```

**Verify:** none in this package. The registration in [Headless.Encryption](#headlessencryption) supplies the implementation.

### Design and runtime behavior

- `IFieldEncryptor.EncryptAsync(ReadOnlyMemory<byte>, FieldContext, CancellationToken)` returns an `EncryptedValue`. `DecryptAsync(EncryptedValue, FieldContext, CancellationToken)` returns a `FieldDecryption` (`Plaintext`, `KeyIsCurrent`, `GetString()`).
- `FieldEncryptorExtensions` adds `EncryptAsync(string, …)` and `DecryptStringAsync(…)`, which use strict UTF-8: a string with a lone surrogate throws `ArgumentException` instead of being rewritten to U+FFFD. For a text column it adds `EncryptToTextAsync(string, …)` and `DecryptTextAsync(string, …)`, which store the frame as `ToBase64Url()` text and read it through `EncryptedValue.Parse`, so unreadable text throws `FormatException`.
- `FieldDecryptionException.Failure` is `KeyNotFound` (the scope was shredded, or the frame names a key from another store), `AuthenticationFailed` (a different context, or a changed frame), or `UnsupportedAlgorithm`. No path returns partial plaintext.
- `EncryptedValue` equality compares frame bytes. `default(EncryptedValue)` holds no frame (`IsDefault`), and decrypting it throws `ArgumentException`.
- `IBlindIndexer.ComputeAsync(BlindIndexContext, string, CancellationToken)` returns a `BlindIndex` (`KeyVersion`, `Hash`). `RehashAsync(BlindIndexContext, string, int storedKeyVersion, CancellationToken)` returns a `BlindIndex?`: the hash under the active version, or `null` when the stored version is not older. `ComputeCandidatesAsync` returns one `BlindIndex` per stored lookup-key version, newest first.
- `BlindIndex.Hash` is a `byte[]` of `BlindIndexContext.Length` bytes. Compare hashes by content. `BlindIndexContext.MinLength` is 8 and `MaxLength` (the default) is 32. Another length throws `ArgumentOutOfRangeException`.
- For a lookup column that must stay text, `BlindIndex.ToBase64Url()` writes unpadded base64url of the key version (4 bytes, big-endian) followed by the hash. `BlindIndex.Parse` and `TryParse` read it back. One input under one key version always gives the same text, so an equality query works on it. Keep the key version in its own column too when a rehash job must find stale rows.
- `IDataKeyManager`: `EnsureAsync(scope)`, `RotateAsync(scope, kind)` (returns the active key id), `RewrapAsync()` (returns the count re-wrapped), `RetireAsync(scope, DataKeyKind.Lookup, version)` (returns whether it deleted the version), and `ShredAsync(scope)` (returns the count deleted).

### Custom key providers

To add a KEK provider or a key store, implement `IKeyEncryptionKeyProvider` (`Name`, `WrapAsync`, `UnwrapAsync`) or `IDataKeyStore`. Register it from a `Use…` extension member through `HeadlessEncryptionSetupBuilder.RegisterKeyEncryptionKeyProvider` or `RegisterKeyStore`, with an `IEncryptionProviderOptionsExtension`.

- A KEK provider binds the wrapped key to `KeyWrapContext` (scope, key id, kind) where its key service supports it. It returns a `WrappedDataKey.KekId` that names the exact KEK version, so `UnwrapAsync` never depends on which version is current.
- A key store serializes `TryInsertAsync` and `ShredScopeAsync` per scope. An insert racing a shred either commits first or returns `DataKeyInsertResult.ScopeShredded`. Only an insert with `reopenShreddedScope: true` clears the shredded marker. `DeleteSupersededVersionAsync` deletes a version only while a newer version of the same scope and kind exists.

---

## Headless.Encryption

Registration, the cipher, key management, and the local key-encryption key.

```bash
dotnet add package Headless.Encryption
```

```csharp
builder.Services.AddHeadlessEncryption(setup =>
{
    setup.UseSqlServer(connectionString);
    setup.UseLocalKey(options =>
    {
        options.Keys["2026-10"] = builder.Configuration["Secrets:EncryptionKey"]!; // 32 bytes, base64
        options.ActiveKeyId = "2026-10";
    });
    setup.ConfigureOptions(options => options.KeyCacheDuration = TimeSpan.FromMinutes(15));
});
```

`UseLocalKey` has the `IConfiguration`, `Action<LocalKeyEncryptionKeyOptions>`, and `Action<LocalKeyEncryptionKeyOptions, IServiceProvider>` overloads. The configuration shape:

```json
{
  "Headless": {
    "Encryption": {
      "LocalKey": {
        "ActiveKeyId": "2026-10",
        "Keys": { "2026-10": "<base64 of 32 random bytes>" }
      }
    }
  }
}
```

**Verify:** `dotnet app.dll check` fails when `Keys` is empty, a key is not 32 bytes, or `ActiveKeyId` names no key. After `resources provision`, the `warmup` readiness check reports `Healthy`. Creating a tenant's key logs event 4300 `DataKeyCreated`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `EncryptionOptions.KeyCacheDuration` | 1 hour | How long an unwrapped key and a scope's active key id stay cached. It bounds how long another host keeps using a shredded key, or the previous version after a rotation. At most 1 day |
| `EncryptionOptions.KeyCacheSize` | 10,000 | The most unwrapped keys cached. Past it, the earliest-cached keys are dropped and unwrapped again on next use |
| `LocalKeyEncryptionKeyOptions.Keys` | required | Key id → base64 of exactly 32 bytes. Keep retired keys until `RewrapAsync` returns 0 |
| `LocalKeyEncryptionKeyOptions.ActiveKeyId` | required | Must name one of `Keys`. Wraps new data keys |

### Design and runtime behavior

- `AddHeadlessEncryption` throws `InvalidOperationException` with no key store or several, no key-encryption key or several, or on a second call.
- Unwrapped data keys live only in process memory, never in a distributed cache. An evicted key is zeroed once no running operation uses it. Concurrent misses for one key share one unwrap.
- Cached keys expire on the registered `TimeProvider`.
- The local KEK wraps with AES-256-GCM under the active key, bound to the data key's scope, id, and kind. Its provider name is `Local`.
- A data key wrapped by another provider (the stored `KekProvider` differs from the configured one) fails with `InvalidOperationException` naming that provider, unless it is kept with `KeepPreviousKeyEncryptionKey`.
- Log events: 4300 `DataKeyCreated`, 4301 `DataKeysShredded` (warning), 4302 `DataKeysRewrapped`, and 4303 `DataKeyRetired`.

---

## Headless.Encryption.AzureKeyVault

Wraps data keys with an RSA key held in Azure Key Vault.

```bash
dotnet add package Headless.Encryption.AzureKeyVault
```

```csharp
builder.Services.AddSingleton(new KeyClient(new Uri("https://pii-kv.vault.azure.net/"), new DefaultAzureCredential()));
builder.Services.AddHeadlessEncryption(setup =>
{
    setup.UsePostgreSql();
    setup.UseAzureKeyVault(builder.Configuration.GetSection("Headless:Encryption:AzureKeyVault")); // { "KeyName": "pii-kek" }
});
```

`UseAzureKeyVault` has the `IConfiguration`, `Action<AzureKeyVaultKeyEncryptionKeyOptions>`, and `Action<AzureKeyVaultKeyEncryptionKeyOptions, IServiceProvider>` overloads, each with an optional `clientFactory` (`Func<IServiceProvider, KeyClient>`). Without one, it uses the `KeyClient` registered in DI.

**Verify:** after `resources provision -k EncryptionKeys`, the `KeyEncryptionKey` warm-up unwraps the host key through the vault and the `warmup` readiness check reports `Healthy`. A missing permission or a wrong key name keeps it `Unhealthy`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `AzureKeyVaultKeyEncryptionKeyOptions.KeyName` | required | An RSA key (2048 or 3072 bits) in the vault the `KeyClient` reaches, with the wrapKey and unwrapKey operations |

### Design and runtime behavior

- New data keys are wrapped with RSA-OAEP-256 under the key's current version. Each key row stores the versioned key identifier, and unwrap uses exactly that version. Rotating the key in Key Vault (a new version) never breaks old keys. Keep old versions enabled until `RewrapAsync` returns 0.
- A wrapped key moved to another row fails to unwrap with `CryptographicException`. Wrapping needs only the public key, so anyone who holds it can wrap a key they chose. A symmetric key wrap (Managed HSM) would close that and is not supported.
- Unwrapping a key wrapped in another vault than the `KeyClient` reaches, or under an identifier without a version, throws `CryptographicException` naming the vault.
- The identity needs only wrapKey and unwrapKey on the key (the "Key Vault Crypto User" role). Headless never creates or deletes vault keys. The provider name stored with each key is `AzureKeyVault`.
- Size the vault for the unwraps described in [Key service load](#key-service-load).

---

## Headless.Encryption.EntityFramework

Maps `EncryptedValue` properties to binary columns in EF Core. It stores the frame and never decrypts.

```bash
dotnet add package Headless.Encryption.EntityFramework
```

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddEncryptedValueConversions();
    }
}

public sealed class Beneficiary
{
    public Guid Id { get; init; }
    public EncryptedValue NationalId { get; set; } // required column
    public EncryptedValue? Phone { get; set; } // optional column
}
```

**Verify:** the EF migration for the entity shows `bytea` (PostgreSQL) or `varbinary(max)` (SQL Server) for each `EncryptedValue` column. A test that saves and reloads the entity gets an equal `EncryptedValue`.

### Design and runtime behavior

- `AddEncryptedValueConversions()` applies `EncryptedValueConverter` to every `EncryptedValue` and `EncryptedValue?` property: `bytea` on PostgreSQL, `varbinary(max)` on SQL Server. For one property, use `HasConversion<EncryptedValueConverter>()`. The converter shares the frame's array instead of copying it, so a caller of `ConvertFromProvider` must not change the array it passes in.
- `BlindIndexConverter` stores a `BlindIndex` as its `ToBase64Url()` text. Apply it per property with `HasConversion<BlindIndexConverter>()`, or to every `BlindIndex` and `BlindIndex?` property with `configurationBuilder.AddBlindIndexConversions()` in `ConfigureConventions`. Nothing applies it by default, because a hash column is usually binary. Call the convention only when the model's lookup columns are text.
- Saving the default `EncryptedValue` throws `InvalidOperationException`, because it holds no frame. Encrypt before saving, or map the column as `EncryptedValue?`, which stores null. Saving the default `BlindIndex` throws the same way. Map an optional hash column as `BlindIndex?`.
- Reading bytes that are not a frame throws `FormatException` when the entity is materialized.
- Equality on an encrypted column compares ciphertext, so never query it for a value. Search through a blind index.

---

## Headless.Encryption.PostgreSql

PostgreSQL storage for the wrapped data keys.

```bash
dotnet add package Headless.Encryption.PostgreSql
```

```csharp
builder.Services.AddHeadlessEncryption(setup =>
{
    setup.UsePostgreSql(connectionString);
    // Or reuse the connection from services.AddPostgreSqlSql(connectionString): setup.UsePostgreSql();
    // Or bind options: setup.UsePostgreSql(builder.Configuration.GetSection("Encryption:Keys"));
    setup.UseLocalKey(builder.Configuration.GetSection("Headless:Encryption:LocalKey"));
});
```

**Verify:** `dotnet app.dll resources provision` creates the tables and the host keys, and `resources verify` then passes. A host started without it fails, naming `resources provision`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The application's database by default. A separate database is allowed |
| `CommandTimeout` | 30 seconds | |
| `Schema` / `TableName` / `ScopesTableName` | `headless` / `encryption_keys` / `encryption_scopes` | Validated as PostgreSQL identifiers. The two table names must differ |

### Design and runtime behavior

- The key table holds `key_id uuid` (primary key), `scope varchar(200) COLLATE "C"`, `kind smallint`, `version integer`, `kek_provider varchar(64)`, `kek_id varchar(1000)`, `wrapped_key bytea`, and `created_at timestamptz`, unique on (`scope`, `kind`, `version`). The scopes table holds `scope` (primary key), `shredded_at timestamptz` (null unless shredded), and `created_at timestamptz`.
- Each write opens its own connection and a short READ COMMITTED transaction, so it never joins the caller's transaction. A transient fault raised before the commit is retried. Each read is one statement on its own connection.
- The tables are one schema step that `resources provision` applies through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts). It is recorded in `headless_schema_history` as `Encryption/1`, with the configured table names appended when they differ from the defaults. A table dropped by hand is not recreated until that history row is deleted too.

---

## Headless.Encryption.SqlServer

SQL Server storage for the wrapped data keys.

```bash
dotnet add package Headless.Encryption.SqlServer
```

```csharp
builder.Services.AddHeadlessEncryption(setup =>
{
    setup.UseSqlServer(connectionString); // or setup.UseSqlServer() with services.AddSqlServerSql(connectionString)
    setup.UseLocalKey(builder.Configuration.GetSection("Headless:Encryption:LocalKey"));
});
```

**Verify:** `dotnet app.dll resources provision` creates the tables and the host keys, and `resources verify` then passes. A host started without it fails, naming `resources provision`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The application's database by default. A separate database is allowed |
| `CommandTimeout` | 30 seconds | |
| `Schema` / `TableName` / `ScopesTableName` | `headless` / `EncryptionKeys` / `EncryptionScopes` | Validated as SQL Server identifiers. The two table names must differ |

### Design and runtime behavior

- The key table holds `KeyId uniqueidentifier` (nonclustered primary key), `Scope nvarchar(200) COLLATE Latin1_General_100_BIN2`, `Kind smallint`, `Version int`, `KekProvider nvarchar(64)`, `KekId nvarchar(1000)`, `WrappedKey varbinary(max)`, and `CreatedAt datetimeoffset(7)`, with a clustered unique index on (`Scope`, `Kind`, `Version`). The scopes table holds `Scope` (clustered primary key), `ShreddedAt datetimeoffset(7)` (null unless shredded), and `CreatedAt datetimeoffset(7)`.
- Each write opens its own connection and a short transaction, so it never joins the caller's transaction. A transient fault raised before the commit is retried. Each read is one statement on its own connection.
- The tables are one schema step recorded as `Encryption/1`, as on PostgreSQL.
