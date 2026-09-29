# Headless.Idempotency.Core

Implements provider-agnostic durable idempotency over an `IIdempotencyRecordStore` whose records carry their own lease.

## Why use this package

Provides `AddHeadlessIdempotency`, tenant-keyed idempotency records, the admission decision (admitted, in flight, replay, or conflict), fenced completion and release against the record's own lease, and the retention purge, without binding to a database provider.

## Install

```bash
dotnet add package Headless.Idempotency.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Idempotency guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/idempotency.md#headlessidempotencycore)
