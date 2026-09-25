# Headless.Sequences.SqlServer

Stores tenant-scoped sequence counters in SQL Server, for both fast and gap-free numbering.

## Why use this package

Issues consecutive per-tenant numbers (receipts, invoices, case numbers) from one atomic update-or-insert batch, and takes gap-free numbers inside the caller's SQL Server unit-of-work transaction so a rollback returns them.

## Install

```bash
dotnet add package Headless.Sequences.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Sequences guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sequences.md#headlesssequencessqlserver)
