# Headless.Encryption.EntityFramework

Maps `EncryptedValue` properties to binary columns in Entity Framework Core, so entities hold the ciphertext frame directly.

## Why use this package

Lets an entity declare an encrypted column as `EncryptedValue` instead of `byte[]`, while encryption and decryption stay explicit calls to `IFieldEncryptor` at the service boundary.

## Install

```bash
dotnet add package Headless.Encryption.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Encryption guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/encryption.md#headlessencryptionentityframework)
