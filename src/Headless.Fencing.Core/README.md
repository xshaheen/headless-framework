# Headless.Fencing.Core

Implements provider-agnostic fenced leases over an `ILeaseStore`.

## Why use this package

Provides `AddHeadlessFencing`, tenant-keyed lease identities, grant-duration bounds, the expired-lease sweep, and the checks every enlisted lease call runs before it joins the caller's transaction, without binding to a database provider.

## Install

```bash
dotnet add package Headless.Fencing.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessfencingcore)
