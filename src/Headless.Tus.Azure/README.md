# Headless.Tus.Azure

Azure Blob Storage TUS store implementation.

## Why use this package

Provides `TusAzureStore`, a complete `ITusStore` implementation that backs resumable uploads with Azure Blob Storage block blobs. Supports all major TUS extensions: Creation, CreationDeferLength, Concatenation, Expiration, Checksum, and Termination.

Select it with `UseAzure` inside `AddHeadlessTus`. It records when a finished upload was handed off to the application, so a failed completion handler runs again, and it keeps each tenant's uploads under their own blob prefix when uploads are scoped by tenant.

## Install

```bash
dotnet add package Headless.Tus.Azure
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [TUS (Resumable Uploads) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/tus.md#headlesstusazure)
