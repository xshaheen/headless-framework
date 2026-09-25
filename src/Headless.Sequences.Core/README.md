# Headless.Sequences.Core

Implements provider-agnostic sequence numbering over an `ISequenceStore`.

## Why use this package

Provides `AddHeadlessSequences`, per-name numbering policies, tenant-keyed counters, and the checks that keep fast and gap-free counters apart, without binding to a database provider.

## Install

```bash
dotnet add package Headless.Sequences.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Sequences guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sequences.md#headlesssequencescore)
