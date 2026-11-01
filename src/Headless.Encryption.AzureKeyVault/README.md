# Headless.Encryption.AzureKeyVault

Wraps the data keys of Headless field-level encryption with a key held in Azure Key Vault.

## Why use this package

Keeps the key-encryption key inside Key Vault, so a database dump or backup alone cannot decrypt personal data, and binds each wrapped key to its tenant, id, and kind so it cannot be moved between rows.

## Install

```bash
dotnet add package Headless.Encryption.AzureKeyVault
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Encryption guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/encryption.md#headlessencryptionazurekeyvault)
