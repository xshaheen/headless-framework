# Headless.Blobs.Abstractions

Defines the unified interfaces and value types for blob/file storage operations across all providers.

## Why use this package

Application code needs a single, provider-agnostic API for file storage so it can switch between cloud providers, local storage, or test fakes without change. This package defines `IBlobStorage`, the `BlobLocation` address type, and the supporting contracts; it carries no implementation and no DI registrations.

## Install

```bash
dotnet add package Headless.Blobs.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Blob Storage guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/blobs.md#headlessblobsabstractions)
