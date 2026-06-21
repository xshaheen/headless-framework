# Headless.Security.Abstractions

Security contracts and option models for string encryption and hashing — no implementation, no DI coupling.

## Problem Solved

Allows downstream packages and application layers to depend on encryption and hashing abstractions without referencing a concrete implementation. `Headless.Settings.Core` depends on `IStringEncryptionService` from this package; consuming code can swap the implementation independently.

## Key Features

- **`IStringEncryptionService`** — AES-GCM authenticated encryption contract:
    - `Encrypt(string? plainText, string? passPhrase = null, byte[]? salt = null) → string?` — encrypts using the configured default pass phrase / salt, or an explicit override. Returns `null` when `plainText` is `null`. Each call uses a fresh random nonce, so identical plaintexts never produce identical cipher text.
    - `Decrypt(string? cipherText, string? passPhrase = null, byte[]? salt = null) → string?` — decrypts a Base64 value produced by `Encrypt`. Returns `null` when `cipherText` is `null` or empty. Throws `CryptographicException` when the cipher text is too short, has been tampered with, or the pass phrase / salt does not match.
- **`IStringHashService`** — deterministic PBKDF2 hashing contract:
    - `Create(string value, string? salt = null) → string` — returns a Base64 PBKDF2 hash. Uses `StringHashOptions.DefaultSalt` when `salt` is omitted; falls back to an empty salt when no default is configured. The hash is deterministic: same value + salt always yield the same output. **Not suitable for password storage** (no per-record random salt, no verification primitive — use ASP.NET Core's `PasswordHasher<T>` for passwords).
- **`StringEncryptionOptions`** — `DefaultPassPhrase` (required), `DefaultSalt` (required `byte[]`), `KeySize` (128/192/256 bits; default 256), `Iterations` (PBKDF2 rounds; default 600 000).
- **`StringHashOptions`** — `Algorithm` (SHA256/SHA384/SHA512; default SHA256), `SizeInBytes` (≥16; default 32), `Iterations` (default 600 000), `DefaultSalt` (optional string).

## Installation

```bash
dotnet add package Headless.Security.Abstractions
```

## Quick Start

```csharp
// Inject the contracts; the implementations are registered by Headless.Security.
public sealed class SecureSettingService(
    IStringEncryptionService encryption,
    IStringHashService hashing)
{
    // Encrypt a sensitive value before writing to the database.
    public string Protect(string value) => encryption.Encrypt(value)!;

    // Decrypt a value read from the database.
    public string Unprotect(string cipher) => encryption.Decrypt(cipher)!;

    // Produce a deterministic lookup hash (blind index over an encrypted column).
    public string BlindIndex(string value, string tenantSalt) => hashing.Create(value, tenantSalt);
}
```

## Configuration

No configuration required. This is an abstractions-only package; options are configured when registering the implementation via `Headless.Security`.

## Dependencies

None.

## Side Effects

None.
