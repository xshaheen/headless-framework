# Headless.Blobs.Azure

Azure Blob Storage implementation of `IBlobStorage` for storing files in Azure.

## Why use this package

Provides integration with Azure Blob Storage using the unified `IBlobStorage` abstraction, with `BlobServiceClient` resolution from DI or a per-store factory, presigned SAS URL support, and an opt-in container-lifecycle capability.

## Install

```bash
dotnet add package Headless.Blobs.Azure
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Blob Storage guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/blobs.md#headlessblobsazure)
