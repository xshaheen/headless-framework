# Headless.Encryption.PostgreSql

Stores the wrapped data keys of Headless field-level encryption in PostgreSQL.

## Why use this package

Keeps the wrapped keys in the application's own PostgreSQL database by default, so tenant creation and backups stay consistent with the encrypted data, with race-safe key creation across hosts.

## Install

```bash
dotnet add package Headless.Encryption.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Encryption guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/encryption.md#headlessencryptionpostgresql)
