# Headless.Caching.Core

Shared factory-backed cache orchestration for cache providers.

## Why use this package

Centralizes the `GetOrAddAsync` state machine so memory, Redis, and hybrid providers share the same factory execution, keyed locking, fail-safe fallback, timeout, eager refresh, conditional refresh, and background completion behavior.

## Install

```bash
dotnet add package Headless.Caching.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Caching guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/caching.md#headlesscachingcore)
