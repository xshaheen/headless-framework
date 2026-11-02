# Headless.Encryption.SqlServer

Stores the wrapped data keys of Headless field-level encryption in SQL Server.

## Why use this package

Keeps the wrapped keys in the application's own SQL Server database by default, so tenant creation and backups stay consistent with the encrypted data, with race-safe key creation across hosts.

## Install

```bash
dotnet add package Headless.Encryption.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Encryption guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/encryption.md#headlessencryptionsqlserver)
