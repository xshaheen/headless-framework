# Headless.Encryption.Abstractions

Defines the field-level envelope encryption contracts: `IFieldEncryptor`, the `EncryptedValue` ciphertext frame, `IDataKeyManager`, and the provider interfaces `IKeyEncryptionKeyProvider` and `IDataKeyStore`.

## Why use this package

Lets domain and application code encrypt and decrypt personal data, and hold its ciphertext, without referencing a key-management service or a database provider.

## Install

```bash
dotnet add package Headless.Encryption.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Encryption guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/encryption.md#headlessencryptionabstractions)
