---
domain: Security
packages: Security.Abstractions, Security, Security.Argon2
---

# Security

> String encryption, deterministic lookup hashes, and verify-capable secret hashing (Argon2id or PBKDF2) behind `Headless.Security` contracts.

## Orientation

- **`Headless.Security.Abstractions`**: the contracts and options, all in the `Headless.Security` namespace. They are `IStringEncryptionService`, `ILookupHasher`, `ISecretHasher`, `SecretVerification`, their option types, the `PhcString` codec, and the `SecretHashAlgorithms` and `SecretHashLimits` constants.
- **`Headless.Security`**: the default implementations and the registration helpers.
  - `AddStringEncryptionService(...)` registers AES-GCM encryption.
  - `AddLookupHasher(...)` registers the deterministic PBKDF2 lookup digest.
  - `AddHeadlessSecretHasher(setup => …)` registers `ISecretHasher` and a startup check. The setup builder selects exactly one algorithm for new hashes, such as `setup.UsePbkdf2Sha256()`.
- **`Headless.Security.Argon2`**: adds `setup.UseArgon2id()`, Argon2id through libsodium (NSec). It is a separate package because it carries a native library.

Choose by what the stored value must do:

| You need to… | Use |
| --- | --- |
| Store a value and read it back later (tokens, connection strings, settings) | `IStringEncryptionService` |
| Find a row by a value without storing the value (a blind index over an encrypted column) | `ILookupHasher`, which is deterministic |
| Store a secret you only ever check, never read back (PINs, API-key secrets, recovery codes, passwords outside ASP.NET Core Identity) | `ISecretHasher` |

## Agent Rules

- Never store a secret with `ILookupHasher`. It is a deterministic digest with no per-record salt and no cost parameter in its output. Use `ISecretHasher` for anything that is only verified.
- Register with `services.AddHeadlessSecretHasher(setup => setup.UseArgon2id())`. Choose `setup.UsePbkdf2Sha256()` instead only when a native dependency is unacceptable (FIPS-only hosts, platforms libsodium does not cover). The builder refuses zero or several `Use*` calls, and a second `AddHeadlessSecretHasher` call.
- Configure cost parameters on the `Use*` call (`UseArgon2id(o => o.MemorySize = …)`), and the shared settings with `setup.Configure(...)`. Each algorithm owns its own options type.
- Store the whole string `Hash` returns in one column. It carries the algorithm, cost, and salt, so do not add salt or version columns.
- Always persist `SecretVerification.Rehashed` when it is non-null. That is how records move to a stronger algorithm or cost; nothing else migrates them.
- Treat the stored hash column as untrusted input. `Verify` already bounds stored parameters, so do not wrap it in a catch-all. A malformed value fails verification and does not throw.
- When the record does not exist, verify against a fixed dummy hash made with the configured parameters, then fail. Otherwise a missing account answers faster than a wrong secret and reveals which accounts exist.
- A 6-digit PIN has only 10⁶ possibilities. No hash parameters protect it from an offline attacker, so pair PIN verification with rate limiting and lockout.
- A high-entropy API key (at least 128 random bits) gains nothing from a slow hash, and hashing one on every request costs latency and CPU. Prefer an HMAC over such keys; use `ISecretHasher` for low-entropy secrets.
- Register `IStringEncryptionService` before `AddHeadlessSettings(...)`: `Headless.Settings.Core` requires it. The recommended way is to bind `Headless:StringEncryption` with `AddStringEncryptionService(...)`.
- `AddStringEncryptionService` and `AddLookupHasher` are idempotent: the first call wins and later calls are ignored, so configure each service once. `AddHeadlessSecretHasher` throws on a second call.

## Secret hashing

### PHC format

`ISecretHasher.Hash` returns a [PHC string](https://github.com/C2SP/C2SP/blob/main/phc-strings.md) with a new 16-byte random salt on every call. It uses standard base64 without padding:

```text
$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
$pbkdf2-sha256$i=600000,l=32$<salt>$<hash>
```

- Argon2id always has `p=1` and a 16-byte salt. libsodium derives only that shape, so Argon2 hashes produced elsewhere with `p>1`, and Argon2i/Argon2d hashes, fail verification.
- The PBKDF2 identifier follows the RustCrypto/Auth0 convention. PHC defines none, and passlib's `$pbkdf2-sha256$` form is a different, non-PHC encoding that this parser rejects.
- `PhcString.TryParse` accepts only canonical encodings: no padding, no non-zero unused base64 bits, no leading-zero decimals, no duplicate parameters, no parameter named `v` (reserved for the version segment, so no encoding is ambiguous), and at most 512 characters. The constructor refuses the same.

### Verification and rotation

```csharp
// sign-up or reset
account.PinHash = hasher.Hash(pin);

// sign-in
var result = hasher.Verify(pin, account.PinHash);

if (!result.Succeeded)
{
    return Results.Unauthorized();
}

if (result.Rehashed is { } upgraded)
{
    account.PinHash = upgraded; // stored algorithm or cost was below the configured one
    await db.SaveChangesAsync(cancellationToken);
}
```

- `Verify` accepts every registered algorithm, whichever one writes. PBKDF2 is always registered for verification, so moving from `UsePbkdf2Sha256()` to `UseArgon2id()`, or raising a cost, never locks anyone out. Records upgrade on their next successful sign-in.
- `Rehashed` is set only after a success, and only when the stored algorithm differs from the configured one or any stored parameter (cost, salt length, hash length) is below the configured value. A stored hash at or above every configured value is never downgraded.
- The comparison is constant-time over the derived bytes. Verification always derives at the stored hash length, so the work does not depend on the length of the supplied secret.
- Outside a host, the startup check never runs. There, a successful `Verify` that needs a rehash under an unregistered selected algorithm throws `InvalidOperationException` from the rehash step.

### Untrusted stored parameters

Anyone who can write the hash column chooses the cost parameters `Verify` runs with. Before deriving anything, verification rejects an encoding outside `SecretHashLimits`:

| Parameter | Accepted range |
| --- | --- |
| Argon2id memory `m` | 8 – 262,144 KiB (256 MiB) |
| Argon2id iterations `t` | 1 – 16 |
| PBKDF2 iterations `i` | 1 – 10,000,000 |
| Salt / hash length | 16 – 64 bytes (Argon2id salt exactly 16) |
| Secret length | `MaxSecretLength` (default 1024 chars, at most 16,384) |

A rejected encoding, or a libsodium failure such as an allocation failure, returns `SecretVerification.Failed`. The options validator applies the same ranges, so every configured value can also be verified.

### Tuning and the startup cost check

The defaults follow the OWASP Password Storage Cheat Sheet:
- Argon2id: `m=19456` (19 MiB), `t=2`, `p=1`, 32-byte hash.
- PBKDF2-SHA256: 600,000 iterations, 16-byte salt, 32-byte hash.

When raising cost, prefer memory for Argon2id. Measure on production hardware: a verification should take tens of milliseconds, not hundreds.

At host start, `SecretHasherStartupValidationService` makes two checks:

1. It fails startup when the algorithm the builder selected has no registered implementation, which only a faulty custom `Use*` extension can cause. This check is cheap, runs in `StartingAsync` before other hosted services, and is not affected by `CostCheck.Mode`.
2. It benchmarks the selected algorithm: one warm-up hash, then the median of three. It compares the median against `CostCheck.MinimumDuration` (default 5 ms) and `CostCheck.MaximumDuration` (default 1 s). `CostCheck.Mode` decides what happens:
    - `Warn` (the default): the benchmark runs in the background once the host has started, so it never delays startup, and an out-of-range result logs a warning. Stopping the host cancels it between samples.
    - `Strict`: the benchmark runs in `StartingAsync` and an out-of-range result fails startup. Every instance then waits for four hashes before it serves traffic, and one instance on a busy node can fail its start although the parameters are fine, so prefer `Strict` for a staging or CI smoke check over every production replica.
    - `Off`: nothing is measured.

The benchmark runs on every instance, because each instance verifies on its own hardware. The mode does not depend on the environment; set it per environment through configuration, for example `Headless:SecretHasher:CostCheck:Mode` = `Off` in `appsettings.Testing.json`. In tests, set low costs and `CostCheck.Mode = SecretHasherCostCheckMode.Off`: the default `Warn` would log a warning for test-grade parameters.

---

## Headless.Security.Abstractions

Security contracts and option models. There is no implementation and no DI coupling.

### API and behavior

- **`IStringEncryptionService`**: an AES-GCM authenticated encryption contract.
    - `Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null) → string?` encrypts with the configured default pass phrase and salt, or with an explicit override. It returns `null` when `plainText` is `null`. Every call uses a fresh random nonce.
    - `Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null) → string?` returns `null` for `null` or empty input. It throws `CryptographicException` when the cipher text is too short, has been tampered with, or the pass phrase or salt does not match.
- **`ILookupHasher`**: a deterministic PBKDF2 digest for lookups.
    - `Create(string value, string? salt = null) → string` returns a Base64 PBKDF2 hash. It uses `LookupHasherOptions.DefaultSalt` when `salt` is omitted, and an empty salt when no default is configured.
    - It is not for secret storage; use `ISecretHasher` for that.
- **`ISecretHasher`**: verify-capable secret hashing.
    - `Hash(ReadOnlySpan<char> secret) → string` returns a new PHC encoding on every call. It throws `ArgumentException` for an empty secret, one longer than `MaxSecretLength`, or one that is invalid UTF-16 (a lone surrogate). It throws `InvalidOperationException` when the selected algorithm is not registered.
    - `Verify(ReadOnlySpan<char> secret, string encoded) → SecretVerification` never throws for malformed, unknown, or out-of-bounds encodings. It throws `ArgumentNullException` for a `null` `encoded`.
- **`SecretVerification(bool Succeeded, string? Rehashed)`**: the result of `Verify`. `SecretVerification.Failed` is the failure value.
- **`SecretHasherOptions`**: the settings shared by every algorithm: `MaxSecretLength` (default 1024) and `CostCheck` (`Mode` `Warn`, `MinimumDuration` 5 ms, `MaximumDuration` 1 s). Cost parameters live on each algorithm's own options (`Pbkdf2Sha256HashOptions`, `Argon2idHashOptions`).
- **`PhcString`**: the canonical PHC codec. `TryParse`, a constructor, `ToString`, `Id`, `Version`, `Parameters`, `Salt`, `Hash`, and `TryGetInt32(name, out value)`, which reads only canonical decimals.
- **`SecretHashAlgorithms`**: the id constants `Argon2id` and `Pbkdf2Sha256`. **`SecretHashLimits`**: the accepted parameter ranges.
- **`StringEncryptionOptions`**: `DefaultPassPhrase` (required), `DefaultSalt` (required `byte[]`), `KeySize` (128/192/256 bits; default 256), `Iterations` (default 600,000).
- **`LookupHasherOptions`**: `Algorithm` (SHA256/SHA384/SHA512; default SHA256), `SizeInBytes` (at least 16; default 32), `Iterations` (default 600,000), `DefaultSalt` (optional).

### Install

```bash
dotnet add package Headless.Security.Abstractions
```

### Setup and use

```csharp
using Headless.Security;

public sealed class AccountSecrets(IStringEncryptionService encryption, ILookupHasher lookup, ISecretHasher secrets)
{
    public string Protect(string value) => encryption.Encrypt(value)!;

    public string BlindIndex(string value, string tenantSalt) => lookup.Create(value, tenantSalt);

    public string HashRecoveryCode(string code) => secrets.Hash(code);
}
```

### Configuration

None. Options are configured when the implementations are registered.

### Runtime behavior

None.

---

## Headless.Security

The default implementations of the Security contracts, the built-in PBKDF2-SHA256 secret-hashing algorithm, and the registration helpers.

### API and behavior

- **`StringEncryptionService`** implements AES-GCM with PBKDF2-SHA256 key derivation. It derives the default key once at construction and re-derives per call only for pass-phrase or salt overrides. Output: `Base64(nonce[12] || tag[16] || cipherText)`.
- **`LookupHasher`** uses `Rfc2898DeriveBytes.Pbkdf2`. Output: `Base64(hash[SizeInBytes])`.
- **`AddStringEncryptionService`** and **`AddLookupHasher`** each have three overloads: `IConfiguration`, `Action<TOptions>`, and `Action<TOptions, IServiceProvider>`. Both are idempotent.
- **`AddHeadlessSecretHasher(Action<HeadlessSecretHasherSetupBuilder>)`** registers `ISecretHasher`, validated `SecretHasherOptions`, the startup check, and PBKDF2-SHA256 for verification. The builder:
    - `Configure(...)` (the same three overloads) binds `SecretHasherOptions`.
    - `UsePbkdf2Sha256()`, plus `IConfiguration`, `Action<Pbkdf2Sha256HashOptions>`, and `Action<Pbkdf2Sha256HashOptions, IServiceProvider>` overloads, selects PBKDF2-SHA256 for new hashes.
    - Exactly one `Use*` call is required; zero, several, or a second `AddHeadlessSecretHasher` throws `InvalidOperationException` at registration.
- **`Pbkdf2Sha256HashOptions`**: `Iterations` (default 600,000), `SaltSize` (16), `HashSize` (32).
- **Adding an algorithm.** Implement `ISecretHashAlgorithm` (`Id`, `Hash(secret)`, `TryComputeHash(secret, encoded, destination)`, `NeedsRehash(encoded)`) with its own options type, and an `ISecretHashAlgorithmOptionsExtension` whose `AddServices` registers the algorithm with `TryAddEnumerable` as a singleton plus its options. Expose it as a `Use{Algorithm}` extension on `HeadlessSecretHasherSetupBuilder` that calls `RegisterExtension`. Read `IOptions<TOptions>.Value` at call time, not in the constructor, and make `TryComputeHash` refuse out-of-range encodings and never throw.

### Design constraints

- **Registration.** `AddStringEncryptionService` and `AddLookupHasher` check for a prior registration and use `TryAdd*`, so a second call is silently ignored. `AddHeadlessSecretHasher` refuses a second call instead, because two calls could select two different algorithms.
- **AES-GCM nonce.** Every `Encrypt` call generates a fresh random 12-byte nonce.
- **Secret handling.** `ISecretHasher` encodes the secret as strict UTF-8 into a stack or pooled buffer and zeroes that buffer when it is done. It zeroes derived bytes too.
- **Algorithm dispatch.** Verification dispatches on the PHC id of the stored hash. The selected algorithm only decides new hashes and rehashes. PBKDF2 always verifies; another algorithm verifies only while it is the selected one.

### Install

```bash
dotnet add package Headless.Security
```

### Setup and use

```csharp
// String encryption: appsettings section "Headless:StringEncryption".
builder.Services.AddStringEncryptionService(builder.Configuration.GetSection("Headless:StringEncryption"));

// Deterministic lookup hash.
builder.Services.AddLookupHasher(options => options.DefaultSalt = "global-app-salt");

// Secret hashing with Argon2id (needs Headless.Security.Argon2).
builder.Services.AddHeadlessSecretHasher(setup =>
{
    setup.Configure(builder.Configuration.GetSection("Headless:SecretHasher"));
    setup.UseArgon2id(builder.Configuration.GetSection("Headless:SecretHasher:Argon2id"));
});

// Or PBKDF2, with no native dependency.
builder.Services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256());
```

### Configuration

`SecretHasherOptions` and the selected algorithm's options, for example:

```json
{
  "Headless": {
    "SecretHasher": {
      "MaxSecretLength": 1024,
      "CostCheck": { "Mode": "Warn", "MinimumDuration": "00:00:00.005", "MaximumDuration": "00:00:01" },
      "Argon2id": { "MemorySize": 19456, "Iterations": 2, "HashSize": 32 }
    }
  }
}
```

`StringEncryptionOptions`:

| Property | Default | Constraint |
|---|---|---|
| `DefaultPassPhrase` | — (required) | Non-empty string |
| `DefaultSalt` | — (required) | Non-empty `byte[]` |
| `KeySize` | 256 | 128, 192, or 256 |
| `Iterations` | 600 000 | > 0 |

`LookupHasherOptions`:

| Property | Default | Constraint |
|---|---|---|
| `Algorithm` | `SHA256` | SHA256, SHA384, or SHA512 |
| `SizeInBytes` | 32 | ≥ 16 |
| `Iterations` | 600 000 | > 0 |
| `DefaultSalt` | `null` | Optional string |

FluentValidation checks every option type at startup (`ValidateOnStart`). The secret-hasher ranges are `SecretHashLimits`, listed under [Untrusted stored parameters](#untrusted-stored-parameters).

### Runtime behavior

- Every service registers as a singleton.
- `SecretHasherStartupValidationService` checks the registration in `IHostedLifecycleService.StartingAsync`, before other hosted services start, and only once per host. The cost benchmark runs there too under `Strict`, and in the background after startup under `Warn`. [Tuning and the startup cost check](#tuning-and-the-startup-cost-check) covers what it checks.

---

## Headless.Security.Argon2

Argon2id (RFC 9106) for `ISecretHasher`, through libsodium via `NSec.Cryptography`.

### API and behavior

- **`UseArgon2id()`** on `HeadlessSecretHasherSetupBuilder`, plus `IConfiguration`, `Action<Argon2idHashOptions>`, and `Action<Argon2idHashOptions, IServiceProvider>` overloads, selects Argon2id for new hashes and registers it for verification.
- **`Argon2idHashOptions`**: `MemorySize` in KiB (default 19,456), `Iterations` (2), `HashSize` (32).

### Design constraints

- **Native dependency.** libsodium ships native binaries for win-x64/x86/arm64, linux-x64/arm/arm64, linux-musl-x64/arm/arm64, and osx-x64/arm64. On other platforms (browser-wasm, riscv64, FreeBSD) it fails to load; use PBKDF2 there.
- **Fixed shape.** Parallelism is always 1 and the salt is always 16 bytes. libsodium supports nothing else, and the OWASP baseline already uses one lane.
- **Failure handling.** A libsodium failure during verification, for example when it cannot allocate the requested memory, returns a failed verification instead of throwing.

### Install

```bash
dotnet add package Headless.Security.Argon2
```

### Setup and use

```csharp
builder.Services.AddHeadlessSecretHasher(setup => setup.UseArgon2id(o => o.MemorySize = 47_104));
```

### Configuration

`Argon2idHashOptions`, bound through the `UseArgon2id(IConfiguration)` overload or set in the delegate overloads.

### Runtime behavior

- Registers the Argon2id `ISecretHashAlgorithm` and validated `Argon2idHashOptions` as singletons.
