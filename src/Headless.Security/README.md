# Headless.Security

Default implementations of `IStringEncryptionService` and `IStringHashService`, plus idempotent DI registration helpers.

## Problem Solved

Ships the concrete AES-GCM encryption and PBKDF2 hashing implementations so application code depends only on the `Headless.Security.Abstractions` contracts. Keeps security concerns separate from `Headless.Core` and `Headless.Api`.

## Key Features

- **`StringEncryptionService`** — `IStringEncryptionService` implementation using AES-GCM with PBKDF2-SHA256 key derivation. Derives the default key once at construction; per-call key derivation only when pass phrase / salt overrides are supplied. Output format: `Base64(nonce[12] || tag[16] || cipherText)`.
- **`StringHashService`** — `IStringHashService` implementation using `Rfc2898DeriveBytes.Pbkdf2`. Output: `Base64(hash[SizeInBytes])`. The call-site salt falls back to `StringHashOptions.DefaultSalt ?? string.Empty`.
- **`AddStringEncryptionService(IConfiguration)`** / **`AddStringEncryptionService(Action<StringEncryptionOptions>)`** / **`AddStringEncryptionService(Action<StringEncryptionOptions, IServiceProvider>)`** — three overloads for binding `StringEncryptionOptions`. All are idempotent: the first registration wins.
- **`AddStringHashService(IConfiguration)`** / **`AddStringHashService(Action<StringHashOptions>)`** / **`AddStringHashService(Action<StringHashOptions, IServiceProvider>)`** — three overloads for binding `StringHashOptions`. All are idempotent.

## Design Notes

- **Idempotency.** Both `AddStringEncryptionService` and `AddStringHashService` use `TryAddSingleton` under a prior-registration guard — calling either more than once is safe and the second call is silently ignored. Configure each service exactly once.
- **AES-GCM nonce.** A fresh 12-byte random nonce is generated via `RandomNumberGenerator.Fill` for every `Encrypt` call. This guarantees ciphertext indistinguishability even when the same plaintext is encrypted multiple times with the same key.
- **PBKDF2 key caching.** The default encryption key (derived from `DefaultPassPhrase` + `DefaultSalt` at construction) is cached as a `byte[]` singleton on the service instance. Overriding the pass phrase or salt on a per-call basis re-derives the key inline and is therefore slower. Design for the common case: configure the default key and use overrides only for rare multi-key scenarios.
- **`StringHashService` is not a password hasher.** The hash has no embedded salt, no algorithm identifier, and no cost parameter — it is a fast keyed lookup digest. Do not use it for storing user passwords; use ASP.NET Core's `PasswordHasher<T>` instead.

## Installation

```bash
dotnet add package Headless.Security
```

## Quick Start

### String Encryption

```csharp
// Bind from configuration section (e.g. "Headless:StringEncryption").
builder.Services.AddStringEncryptionService(
    builder.Configuration.GetSection("Headless:StringEncryption")
);

// Or configure inline.
builder.Services.AddStringEncryptionService(options =>
{
    options.DefaultPassPhrase = "your-secret-pass-phrase";
    options.DefaultSalt      = "your-salt-bytes"u8.ToArray();
    // options.KeySize     = 256;      // default
    // options.Iterations  = 600_000;  // default
});
```

### String Hashing

```csharp
builder.Services.AddStringHashService(options =>
{
    options.DefaultSalt = "global-app-salt";
    // options.Algorithm    = HashAlgorithmName.SHA256; // default
    // options.SizeInBytes  = 32;      // default
    // options.Iterations   = 600_000; // default
});

// Usage: produce a blind index for searching an encrypted column.
public string GetSearchKey(string value, string tenantId)
    => _hashService.Create(value, tenantId); // tenant-scoped deterministic hash
```

## Configuration

`StringEncryptionOptions`:

| Property | Default | Constraint |
|---|---|---|
| `DefaultPassPhrase` | — (required) | Non-empty string |
| `DefaultSalt` | — (required) | Non-empty `byte[]` |
| `KeySize` | 256 | 128, 192, or 256 |
| `Iterations` | 600 000 | > 0 |

`StringHashOptions`:

| Property | Default | Constraint |
|---|---|---|
| `Algorithm` | `SHA256` | SHA256, SHA384, or SHA512 |
| `SizeInBytes` | 32 | ≥ 16 |
| `Iterations` | 600 000 | > 0 |
| `DefaultSalt` | `null` | Optional string |

Both option types are validated via FluentValidation at startup (`ValidateOnStart`). A misconfigured `KeySize` or unsupported `Algorithm` is a startup error, not a runtime error.

## Dependencies

- `Headless.Security.Abstractions`
- `Headless.Checks`
- `Headless.Hosting`
- `FluentValidation`

## Side Effects

- `AddStringEncryptionService(...)` registers `IStringEncryptionService` (`StringEncryptionService`) as a singleton and registers validated `StringEncryptionOptions`.
- `AddStringHashService(...)` registers `IStringHashService` (`StringHashService`) as a singleton and registers validated `StringHashOptions`.
