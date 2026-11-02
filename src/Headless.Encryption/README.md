# Headless.Encryption

Encrypts personal data field by field under per-tenant data keys wrapped by a key-encryption key, and ships the local key-encryption key.

## Why use this package

Stores national ids, phone numbers, and other personal data as ciphertext bound to its tenant and column, finds the key by the id in each value, rotates keys without downtime, and destroys a tenant's data by deleting its keys.

## Install

```bash
dotnet add package Headless.Encryption
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Encryption guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/encryption.md#headlessencryption)
