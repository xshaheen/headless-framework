---
domain: Hashing
packages: Hashing.Abstractions, Hashing, Hashing.Argon2
---

# Hashing

> Verify-only secret hashing (Argon2id or PBKDF2-SHA256) behind `ISecretHasher`, with PHC-encoded hashes that upgrade themselves on sign-in.

## Orientation

- **`Headless.Hashing.Abstractions`**: the contracts application code references, all in the `Headless.Hashing` namespace: `ISecretHasher`, `SecretVerification`, `SecretHasherOptions`, the `PhcString` codec, and the `SecretHashAlgorithms` and `SecretHashLimits` constants.
- **`Headless.Hashing`**: the registration and the PBKDF2-SHA256 algorithm. `AddHeadlessSecretHasher(setup => …)` registers `ISecretHasher` and its startup checks. The builder selects exactly one algorithm for new hashes, such as `setup.UsePbkdf2Sha256()`.
- **`Headless.Hashing.Argon2`**: adds `setup.UseArgon2id()`, Argon2id through libsodium (NSec). It is a separate package because it carries a native library.

Choose by what the stored value must do:

| You need to… | Use |
| --- | --- |
| Store a value and read it back later (tokens, connection strings, settings, personal data) | `IFieldEncryptor` from [Encryption](encryption.md) |
| Find a row by a value without storing the value (a blind index over an encrypted column) | `IBlindIndexer` from [Encryption](encryption.md) |
| Store a secret you only check and never read back (PINs, API-key secrets, recovery codes, passwords outside ASP.NET Core Identity) | `ISecretHasher` |

## Agent Rules

- Never store a secret as a blind index. A blind index has no per-record salt and no cost parameter, so it only finds rows. Use `ISecretHasher` for anything you only verify.
- Register with `services.AddHeadlessSecretHasher(setup => setup.UseArgon2id())`. Choose `setup.UsePbkdf2Sha256()` only when a native dependency is unacceptable, such as a FIPS-only host or a platform libsodium does not cover.
- Call `AddHeadlessSecretHasher` once, with exactly one `Use*` call. Zero `Use*` calls, several, or a second `AddHeadlessSecretHasher` call throws `InvalidOperationException` at registration.
- Set cost parameters on the `Use*` call (`UseArgon2id(o => o.MemorySize = …)`). Set the shared settings with `setup.Configure(...)`. Each algorithm has its own options type.
- Store the whole string `Hash` returns in one column. It carries the algorithm, cost, and salt, so add no salt or version column.
- Persist `SecretVerification.Rehashed` whenever it is non-null. That is the only way stored records move to a stronger algorithm or cost.
- Do not wrap `Verify` in a catch-all. A malformed or out-of-range stored value returns a failed verification and does not throw.
- When the record does not exist, verify against a fixed dummy hash made with the configured parameters, then fail. Otherwise a missing account answers faster than a wrong secret and reveals which accounts exist.
- Pair PIN verification with rate limiting and lockout. A 6-digit PIN has 10⁶ values, and no hash parameters protect it from an offline attacker.
- Prefer an HMAC for a high-entropy API key (at least 128 random bits). A slow hash adds nothing to such a key and costs latency and CPU on every request. Use `ISecretHasher` for low-entropy secrets.
- In tests, set low costs and `CostCheck.Mode = SecretHasherCostCheckMode.Off`. The default `Warn` logs a warning for test-grade parameters.

## Secret hashing

### Setup

```csharp
builder.Services.AddHeadlessSecretHasher(setup =>
{
    setup.Configure(builder.Configuration.GetSection("Headless:SecretHasher"));
    setup.UseArgon2id(builder.Configuration.GetSection("Headless:SecretHasher:Argon2id")); // needs Headless.Hashing.Argon2
});

// Or PBKDF2, with no native dependency, in place of the call above:
// builder.Services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256());
```

**Verify:** `dotnet app.dll check` (or `describe`) runs the options validators and fails when a cost parameter is outside `SecretHashLimits`. After start, an out-of-range cost logs the `SecretHasherCostOutOfRange` warning. In a test, `hasher.Verify(secret, hasher.Hash(secret)).Succeeded` is `true`.

### PHC format

`ISecretHasher.Hash` returns a [PHC string](https://github.com/C2SP/C2SP/blob/main/phc-strings.md) with a new 16-byte random salt on every call, in standard base64 without padding:

```text
$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
$pbkdf2-sha256$i=600000,l=32$<salt>$<hash>
```

- Argon2id hashes always have `p=1` and a 16-byte salt. Argon2 hashes made elsewhere with `p>1`, and Argon2i or Argon2d hashes, fail verification.
- The PBKDF2 identifier follows the RustCrypto and Auth0 convention. passlib's `$pbkdf2-sha256$` form is a different encoding, and the parser rejects it. Hashes imported from passlib do not verify.
- `PhcString.TryParse` and the `PhcString` constructor accept only canonical encodings: no padding, no non-zero unused base64 bits, no leading-zero decimals, no duplicate parameters, no parameter named `v`, and at most 512 characters.

### Verification and rotation

```csharp
// Sign-up or reset.
account.PinHash = hasher.Hash(pin);

// Sign-in.
var result = hasher.Verify(pin, account.PinHash);

if (!result.Succeeded)
{
    return Results.Unauthorized();
}

if (result.Rehashed is { } upgraded)
{
    account.PinHash = upgraded; // the stored algorithm or cost was below the configured one
    await db.SaveChangesAsync(cancellationToken);
}
```

- `Verify` picks the algorithm from the stored hash's PHC id. PBKDF2 always verifies. Argon2id verifies only while `UseArgon2id()` is the selected algorithm. Moving from `UsePbkdf2Sha256()` to `UseArgon2id()`, or raising a cost, locks nobody out: records upgrade on their next successful sign-in.
- `Rehashed` is set only after a success. It is set when the stored algorithm differs from the selected one, or when any stored parameter (cost, salt length, hash length) is below the configured value. A stored hash at or above every configured value is never downgraded.
- The comparison is constant-time over the derived bytes. Verification always derives at the stored hash length, so its work does not depend on the length of the supplied secret.
- The secret is encoded as strict UTF-8 into a buffer that is zeroed afterwards, and the derived bytes are zeroed too.
- Outside a host the startup checks never run. There, a successful `Verify` that needs a rehash under an unregistered algorithm throws `InvalidOperationException`.

### Untrusted stored parameters

Anyone who can write the hash column chooses the cost parameters `Verify` runs with. Before deriving anything, `Verify` rejects an encoding outside `SecretHashLimits`:

| Parameter | Accepted range |
| --- | --- |
| Argon2id memory `m` | 8 – 262,144 KiB (256 MiB) |
| Argon2id iterations `t` | 1 – 16 |
| PBKDF2 iterations `i` | 1 – 10,000,000 |
| Salt and hash length | 16 – 64 bytes (Argon2id salt exactly 16) |
| Secret length | `MaxSecretLength` (default 1024 characters, at most 16,384) |

A rejected encoding, or a libsodium failure such as a failed memory allocation, returns `SecretVerification.Failed`. The options validators apply the same ranges, so every configured value also verifies.

### Tuning and the startup cost check

The defaults follow the OWASP Password Storage Cheat Sheet:

- Argon2id: `m=19456` (19 MiB), `t=2`, `p=1`, 32-byte hash.
- PBKDF2-SHA256: 600,000 iterations, 16-byte salt, 32-byte hash.

To raise the cost of Argon2id, raise memory first. Measure on production hardware: one verification should take tens of milliseconds, not hundreds.

Two checks run when the host starts:

1. A startup validator fails the start with `InvalidOperationException` when the selected algorithm has no registered implementation. Only a faulty custom `Use*` extension causes that. `CostCheck.Mode` does not affect this check.
2. A cost benchmark hashes once to warm up, then takes the median of three hashes and compares it with `CostCheck.MinimumDuration` (default 5 ms) and `CostCheck.MaximumDuration` (default 1 s). `CostCheck.Mode` decides what happens:
    - `Warn` (the default): the benchmark runs in the background after the host starts, so it never delays startup. An out-of-range result logs the `SecretHasherCostOutOfRange` warning. A benchmark that cannot hash logs `SecretHasherCostCheckFailed`. Stopping the host cancels the benchmark between samples.
    - `Strict`: the benchmark runs before the host starts, and an out-of-range result fails the start with `InvalidOperationException`. Every instance then waits for four hashes before it serves traffic, and an instance on a busy node can fail its start although the parameters are fine. Use `Strict` for a staging or CI smoke check, not on every production replica.
    - `Off`: nothing is measured.

The benchmark runs on every instance, because each instance verifies on its own hardware. Set the mode per environment through configuration, for example `Headless:SecretHasher:CostCheck:Mode` = `Off` in `appsettings.Testing.json`.

### Custom algorithms

To add an algorithm, implement `ISecretHashAlgorithm` (`Id`, `Hash`, `TryComputeHash`, `NeedsRehash`) with its own options type, and an `ISecretHashAlgorithmOptionsExtension` whose `AddServices` registers the algorithm as a singleton with `TryAddEnumerable`. Expose it as a `Use{Algorithm}` extension on `HeadlessSecretHasherSetupBuilder` that calls `RegisterExtension`. Read `IOptions<TOptions>.Value` at call time, not in the constructor. Make `TryComputeHash` refuse out-of-range encodings and never throw.

---

## Headless.Hashing.Abstractions

The secret-hashing contracts and options, with no implementation. Reference it from code that only calls `ISecretHasher`.

```bash
dotnet add package Headless.Hashing.Abstractions
```

```csharp
using Headless.Hashing;

public sealed class AccountSecrets(ISecretHasher secrets)
{
    public string HashRecoveryCode(string code) => secrets.Hash(code);
}
```

**Verify:** none in this package. The registration in [Headless.Hashing](#headlesshashing) supplies the implementation.

### API and behavior

- `ISecretHasher.Hash(ReadOnlySpan<char> secret)` returns a new PHC string on every call. It throws `ArgumentException` for an empty secret, a secret longer than `MaxSecretLength`, or invalid UTF-16 (a lone surrogate). It throws `InvalidOperationException` when the selected algorithm is not registered.
- `ISecretHasher.Verify(ReadOnlySpan<char> secret, string encoded)` returns a `SecretVerification(bool Succeeded, string? Rehashed)`. It never throws for a malformed, unknown, or out-of-range encoding. It throws `ArgumentNullException` for a `null` `encoded`. `SecretVerification.Failed` is the failure value.
- `SecretHasherOptions` holds the settings every algorithm shares: `MaxSecretLength` (default 1024) and `CostCheck` (`Mode` `Warn`, `MinimumDuration` 5 ms, `MaximumDuration` 1 s).
- `PhcString` parses and writes canonical PHC strings: `TryParse`, a constructor, `ToString`, `Id`, `Version`, `Parameters`, `Salt`, `Hash`, and `TryGetInt32(name, out value)`, which reads only canonical decimals.
- `SecretHashAlgorithms` holds the id constants `Argon2id` and `Pbkdf2Sha256`. `SecretHashLimits` holds the accepted parameter ranges.

---

## Headless.Hashing

The `ISecretHasher` implementation, the PBKDF2-SHA256 algorithm, and the registration.

```bash
dotnet add package Headless.Hashing
```

```csharp
builder.Services.AddHeadlessSecretHasher(setup =>
{
    setup.Configure(builder.Configuration.GetSection("Headless:SecretHasher"));
    setup.UsePbkdf2Sha256(builder.Configuration.GetSection("Headless:SecretHasher:Pbkdf2Sha256"));
});
```

**Verify:** `dotnet app.dll check` passes. A test that resolves `ISecretHasher` gets a hash starting with `$pbkdf2-sha256$`.

### Configuration

`HeadlessSecretHasherSetupBuilder` members:

- `Configure(...)` binds `SecretHasherOptions`, with the `IConfiguration`, `Action<SecretHasherOptions>`, and `Action<SecretHasherOptions, IServiceProvider>` overloads.
- `UsePbkdf2Sha256()` selects PBKDF2-SHA256 for new hashes, with the same three overloads for `Pbkdf2Sha256HashOptions`: `Iterations` (default 600,000), `SaltSize` (16), and `HashSize` (32).

The configuration shape, here with Argon2id:

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

Every options type is validated at startup (`ValidateOnStart`) against the ranges in [Untrusted stored parameters](#untrusted-stored-parameters).

### Runtime behavior

- Every service is a singleton.
- PBKDF2-SHA256 is always registered for verification, whichever algorithm writes new hashes.
- The registration check runs with the other startup validators, before any hosted service starts. The cost benchmark runs once per host, as [Tuning and the startup cost check](#tuning-and-the-startup-cost-check) describes.

---

## Headless.Hashing.Argon2

Argon2id (RFC 9106) for `ISecretHasher`, through libsodium via `NSec.Cryptography`.

```bash
dotnet add package Headless.Hashing.Argon2
```

```csharp
builder.Services.AddHeadlessSecretHasher(setup => setup.UseArgon2id(o => o.MemorySize = 47_104));
```

**Verify:** a test that resolves `ISecretHasher` gets a hash starting with `$argon2id$`.

### Configuration

`UseArgon2id()` on `HeadlessSecretHasherSetupBuilder` selects Argon2id for new hashes and registers it for verification. Its `IConfiguration`, `Action<Argon2idHashOptions>`, and `Action<Argon2idHashOptions, IServiceProvider>` overloads set `Argon2idHashOptions`: `MemorySize` in KiB (default 19,456), `Iterations` (2), and `HashSize` (32).

### Runtime behavior

- **Platforms.** libsodium ships native binaries for win-x64, win-x86, win-arm64, linux-x64, linux-arm, linux-arm64, linux-musl-x64, linux-musl-arm, linux-musl-arm64, osx-x64, and osx-arm64. On other platforms (browser-wasm, riscv64, FreeBSD) it fails to load. Use PBKDF2 there.
- **Fixed shape.** Parallelism is always 1 and the salt is always 16 bytes.
- **Failures.** A libsodium failure during verification, such as a failed memory allocation, returns a failed verification instead of throwing.
