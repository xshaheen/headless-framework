# Headless.AuditLog.Core

DI setup package for `Headless.AuditLog`: options validation, setup builders, and the exactly-one-storage-provider registration pipeline.

## Why use this package

Keeps audit-log contracts provider-neutral while centralizing the public `AddHeadlessAuditLog(...)` setup API and provider extension hook in one Core package.

## Install

```bash
dotnet add package Headless.AuditLog.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Audit Log guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/audit-log.md#headlessauditlogcore)
