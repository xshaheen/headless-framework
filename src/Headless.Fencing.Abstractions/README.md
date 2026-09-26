# Headless.Fencing.Abstractions

Defines the fenced-lease contracts: `IFencedLeases`, `FencedLease`, the grant, renewal, and settlement results, and the `unit.Leases` accessor.

## Why use this package

Lets application code grant a lease, fence its writes against a stale or zombie attempt, and settle the attempt without referencing a database provider.

## Install

```bash
dotnet add package Headless.Fencing.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessfencingabstractions)
