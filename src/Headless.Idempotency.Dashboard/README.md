# Headless.Idempotency.Dashboard

The Idempotency module of the Headless dashboard: the idempotency store's settings and a read-only lookup of one key's record metadata.

## Why use this package

Support staff asked "did this payment go through twice?" otherwise query the record table by hand or call `GetResultAsync`, which returns a stored body that can hold personal data. This module looks one key up through a POST, in a tenant the operator names, and shows whether the operation was admitted, is running, stalled, or completed, with the stored result's contract and size but never its bytes. It offers no release, expire, or delete, because releasing a live attempt could run its operation twice. It is embedded in the host and served by `Headless.Dashboard` under its base path and sign-in.

## Install

```bash
dotnet add package Headless.Idempotency.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Idempotency guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/idempotency.md#headlessidempotencydashboard)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards/idempotency-module.md#idempotency-module)
