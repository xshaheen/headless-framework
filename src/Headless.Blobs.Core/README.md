# Headless.Blobs.Core

Unified setup builder for composing one or more named blob stores in a single DI container.

## Why use this package

A single application often needs several blob stores at once — images on one backend, documents on another, scratch files on a third — and sometimes two instances of the same provider (a production and a staging bucket). Registering providers directly only yields one `IBlobStorage`; a second registration silently shadows the first. This package adds `AddHeadlessBlobs(...)`, a single entry point that composes an optional default plus any number of independently-configured named stores, each resolvable by name.

## Install

```bash
dotnet add package Headless.Blobs.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Blob Storage guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/blobs.md#headlessblobscore)
