# Headless.Sequences.Sqlite

Stores tenant-scoped sequence counters in SQLite, for both fast and gap-free numbering.

## Why use this package

Issues consecutive per-tenant numbers (receipts, invoices, case numbers) from one atomic upsert-increment, and takes gap-free numbers inside the caller's SQLite unit-of-work transaction so a rollback returns them.

## Install

```bash
dotnet add package Headless.Sequences.Sqlite
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Sequences guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sequences.md#headlesssequencessqlite)
