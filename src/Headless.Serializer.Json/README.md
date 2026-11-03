# Headless.Serializer.Json

System.Text.Json implementation of `IJsonSerializer` with opinionated defaults and a rich built-in converter library.

## Why use this package

Provides JSON serialization via System.Text.Json with two presets, one for HTTP APIs and one for stored JSON that reads back what it wrote, and a set of reusable converters for common edge cases — all wired through the `IJsonSerializer` abstraction.

## Install

```bash
dotnet add package Headless.Serializer.Json
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Serialization guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/serialization.md#headlessserializerjson)
