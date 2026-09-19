# Headless.AuditLog.Abstractions

Defines the property-level audit log contracts for tracking entity mutations and explicit business events.

## Why use this package

Provides a provider-agnostic audit log API for representing field-level entity changes and explicit events (PII reveals, cross-tenant access, etc.) without binding consumers to a capture engine or storage implementation.

## Install

```bash
dotnet add package Headless.AuditLog.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Audit Log guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/audit-log.md#headlessauditlogabstractions)
