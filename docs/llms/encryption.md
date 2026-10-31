---
domain: Encryption
packages: Encryption.Abstractions, Encryption, Encryption.EntityFramework, Encryption.PostgreSql, Encryption.SqlServer
---

# Encryption

> Field-level envelope encryption for personal data: each value is AES-256-GCM ciphertext under a per-tenant data key, the data keys are stored wrapped by a key-encryption key (KEK) that never leaves its key service or host, and deleting a tenant's keys destroys its data (crypto-shredding).

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
    public EncryptedValue NationalId { get; set; } // bytea / varbinary(max), mapped by Headless.Encryption.EntityFramework
}

var context = new FieldContext("Beneficiary.NationalId"); // scope = the current tenant
beneficiary.NationalId = await encryptor.EncryptAsync(nationalId, context, ct);

var decryption = await encryptor.DecryptAsync(beneficiary.NationalId, context, ct);
string plaintext = decryption.GetString();
```

- **`Headless.Encryption.Abstractions`**: `IFieldEncryptor`, `FieldContext`, `EncryptedValue`, `FieldDecryption`, `FieldDecryptionException`, `IDataKeyManager`, and the provider interfaces `IKeyEncryptionKeyProvider` and `IDataKeyStore`.
- **`Headless.Encryption`**: `AddHeadlessEncryption`, the cipher, the in-process key cache, `IDataKeyManager`, the host-keys resource, the KEK warm-up, and the local KEK (`UseLocalKey`).
- **`Headless.Encryption.EntityFramework`**: maps `EncryptedValue` properties to binary columns (`configurationBuilder.AddEncryptedValueConversions()`), without decrypting.
- **`Headless.Encryption.PostgreSql`**, **`Headless.Encryption.SqlServer`**: the wrapped-key table.

## Agent Rules

- Encrypt and decrypt explicitly through `IFieldEncryptor`; there is no transparent EF Core value converter. A decrypting converter would run synchronously while a KMS unwrap is async, would see only the ambient tenant (so cross-tenant admin queries fail), would put plaintext in the change tracker and audit capture, and would make equality queries on the column silently match nothing.
- Never query an encrypted column for equality: each encryption of one value differs. Search through a blind index (a keyed hash of the value) stored in its own column.
- Decrypt with the same `FieldContext` the value was encrypted with: same `Purpose`, same scope, same `Binding`. The context is not stored in the frame; that is what stops a value moving between columns, tenants, or rows. A different context throws `FieldDecryptionException` with `Failure = AuthenticationFailed`.
- Keep `Purpose` stable. It selects the column's subkey, so renaming it makes every stored value unreadable. Use the entity and property name (`Beneficiary.NationalId`).
- A `FieldContext` with no `Scope` uses `ICurrentTenant.Id`. With no current tenant it throws `MissingTenantContextException`; it never falls back to the host key, because a value written under the host key would survive the tenant's shredding. Host-level data uses `FieldContext.Host(purpose)`.
- Scopes are tenant ids or per-subject scopes (`{tenant}/{beneficiaryId}`) of at most 200 characters, compared ordinally; leading or trailing white space and NUL characters are refused.
- With EF Core, declare the column as `EncryptedValue` (or `EncryptedValue?` when optional) and call `configurationBuilder.AddEncryptedValueConversions()` in `ConfigureConventions`. Without EF Core, store `EncryptedValue.ToArray()` in a `bytea` / `varbinary(max)` column, or `ToBase64Url()` in a text column, and read it back with `EncryptedValue.FromBytes` / `EncryptedValue.Parse`; both throw `FormatException` for bytes that are not a frame.
- Treat `FieldDecryptionException` with `Failure = KeyNotFound` as "this tenant's data was shredded", not as corruption.
- A shred is final. Encrypting for a shredded scope throws `DataKeyScopeShreddedException` instead of creating a key; reopen the scope deliberately with `IDataKeyManager.EnsureAsync`, which creates new keys and leaves the shredded values unreadable.
- Keep one key-encryption-key provider for the life of the key table. Switching providers (for example from `UseLocalKey` to a key service) is not supported yet: data keys wrapped by the old provider can no longer be unwrapped, and `RewrapAsync` cannot move them.
- Keep KEK material out of the database and out of source control. With `UseLocalKey`, whoever reads the configured key reads every value; prefer a key service in production when one is available.
- Run `resources provision` (or `deploy`) on every release: it creates the key table and the host-scope keys. A host start only verifies them.

## Core Concepts

### Key hierarchy

```text
KEK (key service, HSM, or a local 32-byte key; never stored by Headless)
 └─ wraps → data key per (scope, kind, version): 32 random bytes, stored wrapped
      ├─ kind Data   → HKDF-SHA256(data key, info = "headless/fe/v1\0" + purpose) → AES-256-GCM per value
      └─ kind Lookup → reserved for the scope's blind-index key
```

A data key is random and stored, not derived, so deleting its row destroys it. HKDF below the data key gives each purpose its own GCM key, which keeps each column's random-nonce count under its own key.

### The frame

`EncryptedValue` is one binary frame, 46 bytes longer than the plaintext:

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 1 | format version, `0x01` |
| 1 | 1 | algorithm, `0x01` = AES-256-GCM under an HKDF-SHA256 purpose subkey |
| 2 | 16 | data-key id (big-endian UUID) |
| 18 | 12 | random nonce |
| 30 | n | ciphertext |
| 30+n | 16 | GCM tag |

Decryption looks the key up by the id in the frame; it never tries keys in turn. The authenticated data is the first 18 header bytes, then the scope, purpose, and binding, each with a 4-byte big-endian length prefix, so a changed header byte or a moved value fails the tag.

### Scopes, tenants, and binding

| `FieldContext` | Encrypts under | Use for |
| --- | --- | --- |
| `new FieldContext(purpose)` | the current tenant's key | tenant data in a request |
| `new FieldContext(purpose) { Scope = tenantId }` | that tenant's key | jobs and admin work outside a tenant context |
| `FieldContext.Host(purpose)` | the host key (scope `""`) | data that belongs to no tenant |
| `… { Binding = rowIdBytes }` | the same key, also bound to the row | columns where two rows of one tenant must not swap values |

`Binding` is off by default: an id the database generates is unknown when the value is encrypted.

### Key lifecycle

- **Creation.** The first encryption for a scope with no data key creates version 1: one KEK wrap per tenant, ever. Two hosts racing on a new tenant agree on one key through the store's unique (scope, kind, version) key. `IDataKeyManager.EnsureAsync(tenantId)` creates the tenant's Data and Lookup keys up front, so onboarding fails early when the KEK is unreachable.
- **Rotation.** `RotateAsync(scope, DataKeyKind.Data)` adds the next version, which encrypts every new value. Older versions keep decrypting. `FieldDecryption.KeyIsCurrent` is `false` for a value under an older version: a re-encryption job decrypts and encrypts it again. Other hosts switch to the new version when their cached active key expires.
- **KEK rotation.** Make a new KEK version current (for the local key: add a key and point `ActiveKeyId` at it), then run `RewrapAsync()`. It re-wraps every data key the current KEK version did not wrap and returns how many it changed; no encrypted value changes. Keep the old KEK version enabled until a run returns 0.
- **Shredding.** `ShredAsync(scope)` deletes every key of the scope, marks the scope shredded, and returns how many keys it deleted. This host forgets them at once; another host forgets them when its cached copy expires (`EncryptionOptions.KeyCacheDuration`, 1 hour by default). Then every value of the scope fails with `KeyNotFound`.
- **A shred is final.** A later encryption for the scope throws `DataKeyScopeShreddedException`, and so does `RotateAsync`; nothing creates a key for it implicitly. An encryption racing the shred on any host either commits its key first, which the shred then deletes, or is refused. `EnsureAsync(scope)` is the only way back: it clears the marker and creates new keys, and the values from before the shred stay unreadable.

**Backup limit.** A database backup taken before a shred still holds the wrapped keys, and restoring it while the shared KEK exists recovers the data. Shredding is immediate on live systems and complete once backup retention passes. A guarantee against backups needs a per-tenant KEK deleted in the key service; the stored KEK id per key leaves that possible without a schema change.

A per-tenant KEK adds only that backup guarantee; it does not shred faster. Key services delay deletion on purpose: AWS KMS waits 7 to 30 days (30 by default) before deleting a key, and Azure Key Vault keeps a deleted key recoverable for 7 to 90 days (90 by default), with soft-delete that cannot be turned off and, under purge protection, no early purge. Deleting the data-key rows is faster: immediate on this host, and on every host within `KeyCacheDuration`.

### Deploy and startup

| What | Created by | A starting host |
| --- | --- | --- |
| The KEK | the operator or infrastructure-as-code; Headless never creates one | nothing |
| The key table | `resources provision` (kind `Schema`) | fails the start when it is missing, naming `resources provision` |
| The host-scope keys | `resources provision -k EncryptionKeys` | one read; until it passes, readiness reports the resource missing |
| Tenant keys | `EnsureAsync` during onboarding, or the first encryption | nothing |
| KEK reachability | — | the `KeyEncryptionKey` warm-up unwraps the host Data key once after start; readiness stays `Unhealthy` until it succeeds, and the start never waits on the key service |

The `EncryptionKeys` resource refuses `resources purge` and `resources delete`: removing keys destroys data, so only `ShredAsync` deletes them.

### Key service load

A host calls the KEK about once per data key per `KeyCacheDuration`: an unwrap when a key is first used or its cache entry expires, and a wrap when a key is created. Concurrent misses for one key share a single unwrap. Expect a burst of roughly hosts × active tenants unwraps after a deployment, when every cache starts empty, then a steady trickle as entries expire.

Size the key service for that burst. Azure Key Vault allows 2,000 RSA-2048 or 500 RSA-3072 operations per 10 seconds per vault for HSM-protected keys (twice that for software keys), but only 250 for RSA-4096, so prefer a 2048- or 3072-bit KEK. AWS KMS shares 10,000 to 100,000 symmetric-key requests per second per Region across operations. A longer `KeyCacheDuration` lowers the load, at the cost of slower shredding and rotation on other hosts.

### Limits

- **No key commitment.** AES-GCM is not key-committing: a ciphertext can be crafted to decrypt under two different keys an attacker knows. The scheme does not need it, because the frame names its key by an id inside the authenticated data, and data keys are random keys the host generates, never keys a caller chooses. Adding commitment would cost about 30 bytes per value for no gain under this threat model.
- **Values per key.** Each value gets a random 96-bit nonce, and NIST SP 800-38D allows at most 2³² such encryptions under one key. The subkey is per scope, purpose, and data-key version, so the budget is 2³² values per tenant, column, and version. Rotate the scope's Data key with `RotateAsync` well before a column could approach that; rotation starts a fresh budget.

### Threat model

It protects against a leaked database dump or backup while the KEK stays outside the database, read-only SQL injection that exfiltrates rows, casual database or support access, ciphertext copied across tenants or columns, and tampering. It does not protect against a compromised application host (which holds the KEK permission and the key cache), an attacker with the application's KMS identity, an injection that returns decrypted data, same-tenant same-column row swaps unless `Binding` is set, or someone with database write access corrupting key rows.

## Choosing a Provider

| Key store | Use when | Trade-off |
| --- | --- | --- |
| `Headless.Encryption.PostgreSql` | The application's data is in PostgreSQL | Keys back up with the data |
| `Headless.Encryption.SqlServer` | The application's data is in SQL Server | Keys back up with the data |

| Key-encryption key | Use when | Trade-off |
| --- | --- | --- |
| `UseLocalKey` (in `Headless.Encryption`) | Development, tests, or a host whose secret store holds the key | The host process and its configuration hold the KEK itself |

Keep the key table in the application's database (the default) so tenant creation and backups stay consistent; a separate connection string is allowed.

---

## Headless.Encryption.Abstractions

The contracts application and domain code reference.

### Setup

```bash
dotnet add package Headless.Encryption.Abstractions
```

### Design and runtime behavior

- `IFieldEncryptor.EncryptAsync(ReadOnlyMemory<byte>, FieldContext, CancellationToken)` returns an `EncryptedValue`; `DecryptAsync(EncryptedValue, FieldContext, CancellationToken)` returns `FieldDecryption` (`Plaintext`, `KeyIsCurrent`, `GetString()`). `FieldEncryptorExtensions` adds `EncryptAsync(string, …)` and `DecryptStringAsync(…)`, as UTF-8.
- `FieldDecryptionException.Failure` is `KeyNotFound` (the scope was shredded, or the frame names a key from another store), `AuthenticationFailed` (a different context, or a changed frame), or `UnsupportedAlgorithm`. No path returns partial plaintext.
- `EncryptedValue` equality compares frame bytes. `default(EncryptedValue)` holds no frame (`IsDefault`); decrypting it throws `ArgumentException`.
- `IDataKeyManager`: `EnsureAsync(scope)`, `RotateAsync(scope, kind)` (returns the active key id), `RewrapAsync()` (returns the count re-wrapped), `ShredAsync(scope)` (returns the count deleted).
- **Writing a provider.** `IKeyEncryptionKeyProvider` (`Name`, `WrapAsync`, `UnwrapAsync`) and `IDataKeyStore` are public and need no internal member. A provider package registers through `HeadlessEncryptionSetupBuilder.RegisterKeyEncryptionKeyProvider` or `RegisterKeyStore` with an `IEncryptionProviderOptionsExtension`, from a `Use…` extension member. A KEK provider binds the wrapped key to `KeyWrapContext` (scope, key id, kind) where its key service supports it, and returns a `WrappedDataKey.KekId` that names the exact KEK version, so `UnwrapAsync` never depends on which version is current. A key store makes `TryInsertAsync` and `ShredScopeAsync` serialize per scope: an insert racing a shred either commits before it or returns `DataKeyInsertResult.ScopeShredded`, and only an insert with `reopenShreddedScope: true` clears the shredded marker. New members on both interfaces arrive as default interface members.

---

## Headless.Encryption

Registration, the cipher, key management, and the local key-encryption key.

### Setup

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

`UseLocalKey` has the usual three overloads (`IConfiguration`, `Action<LocalKeyEncryptionKeyOptions>`, `Action<LocalKeyEncryptionKeyOptions, IServiceProvider>`). The configuration shape:

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

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `EncryptionOptions.KeyCacheDuration` | 1 hour | How long an unwrapped key and a scope's active key id stay cached; bounds how long another host keeps using a shredded key or the previous version after a rotation. At most 1 day |
| `EncryptionOptions.KeyCacheSize` | 10,000 | Most unwrapped keys cached; past it, the earliest-cached keys are dropped and unwrapped again on next use |
| `LocalKeyEncryptionKeyOptions.Keys` | required | Key id → base64 of exactly 32 bytes. Keep retired keys until `RewrapAsync` returns 0 |
| `LocalKeyEncryptionKeyOptions.ActiveKeyId` | required | Must name one of `Keys`; wraps new data keys |

### Design and runtime behavior

- `AddHeadlessEncryption` throws `InvalidOperationException` with no or several key stores, no or several key-encryption keys, or a second call.
- It swaps the `NullCurrentTenant` fallback for the AsyncLocal-backed `ICurrentTenant` unless the host registered its own, so `ICurrentTenant.Change` scopes encryption.
- Unwrapped data keys live only in process memory, by key id, never in a distributed cache. An evicted key is cleared with `CryptographicOperations.ZeroMemory` once no running operation uses it. Concurrent misses for one key share one unwrap.
- Cached keys expire on the registered `TimeProvider`, so a test can advance it to see another host's shred take effect.
- The local KEK wraps with AES-256-GCM under the active key, with the data key's scope, id, and kind as authenticated data; a wrapped key is 60 bytes. Its provider name is `Local`.
- A data key wrapped by another provider (the stored `KekProvider` differs from the configured one) fails with `InvalidOperationException` naming both. Moving keys between providers is not supported yet.
- Log events 4300 (`DataKeyCreated`), 4301 (`DataKeysShredded`, warning), and 4302 (`DataKeysRewrapped`).

---

## Headless.Encryption.EntityFramework

Maps `EncryptedValue` properties to binary columns in EF Core. It stores the frame and never decrypts.

### Setup

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

### Design and runtime behavior

- `AddEncryptedValueConversions()` applies `EncryptedValueConverter` to every `EncryptedValue` and `EncryptedValue?` property: `bytea` on PostgreSQL, `varbinary(max)` on SQL Server. A single property can use it directly with `HasConversion<EncryptedValueConverter>()`.
- Saving the default `EncryptedValue` throws `InvalidOperationException`: it holds no frame, and the empty bytes it would store could not be read back. Encrypt before saving, or map the column as `EncryptedValue?`, which stores null.
- Reading bytes that are not a frame throws `FormatException` when the entity is materialized.
- There is no decrypting converter by design: decryption is async and needs the row's tenant, and plaintext in the change tracker would reach audit capture. Equality on the column compares ciphertext, so never query it for a value; search through a blind index.

---

## Headless.Encryption.PostgreSql

PostgreSQL storage for the wrapped data keys.

### Setup

```bash
dotnet add package Headless.Encryption.PostgreSql
```

```csharp
builder.Services.AddHeadlessEncryption(setup =>
{
    setup.UsePostgreSql(connectionString);
    // or reuse the connection from services.AddPostgreSqlSql(connectionString): setup.UsePostgreSql();
    // or bind options: setup.UsePostgreSql(builder.Configuration.GetSection("Encryption:Keys"));
    setup.UseLocalKey(builder.Configuration.GetSection("Headless:Encryption:LocalKey"));
});
```

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The application's database by default; a separate database is allowed |
| `CommandTimeout` | 30 seconds | |
| `Schema` / `TableName` / `ScopesTableName` | `headless` / `encryption_keys` / `encryption_scopes` | Validated as PostgreSQL identifiers; the two table names must differ |

### Design and runtime behavior

- Two tables. The key table: `key_id uuid` primary key, `scope varchar(200) COLLATE "C"`, `kind smallint`, `version integer`, `kek_provider varchar(64)`, `kek_id varchar(1000)`, `wrapped_key bytea`, `created_at timestamptz`, and a unique (`scope`, `kind`, `version`) constraint. The scopes table: `scope` primary key, `shredded_at timestamptz` (null unless shredded), `created_at timestamptz`.
- A key insert and a shred of one scope both create the scope's row if absent and lock it (`FOR NO KEY UPDATE`) before touching keys, so they serialize. The key itself is inserted with `INSERT … ON CONFLICT (scope, kind, version) DO NOTHING`, so racing creators agree on one row. Every call opens its own connection and short READ COMMITTED transaction and retries a transient fault raised before the commit.
- The tables are one step `resources provision` applies through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), recorded in `headless_schema_history` as `Encryption/1` (with each configured table name appended, `Encryption:<table>:<scopes table>/1`).

---

## Headless.Encryption.SqlServer

SQL Server storage for the wrapped data keys.

### Setup

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

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The application's database by default; a separate database is allowed |
| `CommandTimeout` | 30 seconds | |
| `Schema` / `TableName` / `ScopesTableName` | `headless` / `EncryptionKeys` / `EncryptionScopes` | Validated as SQL Server identifiers; the two table names must differ |

### Design and runtime behavior

- Two tables. The key table: `KeyId uniqueidentifier` (nonclustered primary key), `Scope nvarchar(200) COLLATE Latin1_General_100_BIN2`, `Kind smallint`, `Version int`, `KekProvider nvarchar(64)`, `KekId nvarchar(1000)`, `WrappedKey varbinary(max)`, `CreatedAt datetimeoffset(7)`, and a clustered unique (`Scope`, `Kind`, `Version`) index. The scopes table: `Scope` clustered primary key, `ShreddedAt datetimeoffset(7)` (null unless shredded), `CreatedAt datetimeoffset(7)`.
- A key insert and a shred of one scope both create the scope's row if absent and take its update lock before touching keys, so they serialize. The key itself is inserted with an `INSERT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK))`, whose key-range lock on the unique index serializes racing creators. Every call opens its own connection and short transaction and retries a transient fault raised before the commit.
- The tables are one schema-runner step recorded as `Encryption/1`, like PostgreSQL.
