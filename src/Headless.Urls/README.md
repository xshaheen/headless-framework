# Headless.Urls

Fluent, allocation-conscious URL building and parsing.

## Why use this package

Assembling and manipulating URLs with raw string concatenation is error-prone: double slashes, missing
encoding, duplicated query parameters, and fragile parsing. `Headless.Urls` provides a mutable `Url` builder
plus an ordered, multi-value `QueryParamCollection` so path segments, query parameters, and fragments can be
composed and edited without hand-rolling encoding rules.

## Install

```bash
dotnet add package Headless.Urls
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Extensions guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/extensions.md#headlessurls)
