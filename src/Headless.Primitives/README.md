# Headless.Primitives

Value objects, the result pattern, paging models, and domain primitives.

## Why use this package

Domain code that passes raw `Guid`, `decimal`, `(double, double)`, or throws-and-catches for expected failures
loses intent and lets invalid states exist. `Headless.Primitives` supplies the framework's shared building
blocks: a result pattern for expected failures, validated value objects that cannot hold invalid data, and
consistent paging and error-descriptor shapes so every package models these the same way.

## Install

```bash
dotnet add package Headless.Primitives
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Extensions guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/extensions.md#headlessprimitives)
