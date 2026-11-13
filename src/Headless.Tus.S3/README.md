# Headless.Tus.S3

Amazon S3, Cloudflare R2, and S3-compatible TUS store implementation.

## Why use this package

Provides `TusS3Store`, an `ITusStore` implementation that backs resumable uploads with an S3 bucket through the application's own `IAmazonS3` client. It targets Cloudflare R2, Amazon S3, and S3-compatible servers such as MinIO. Supports Creation, CreationDeferLength, Concatenation, Expiration, and Termination; it does not support Checksum.

Select it with `UseS3` inside `AddHeadlessTus`. It records when a finished upload was handed off to the application, so a failed completion handler runs again, and it keeps each tenant's uploads under their own key prefix when uploads are scoped by tenant.

## Install

```bash
dotnet add package Headless.Tus.S3
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [TUS (Resumable Uploads) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/tus.md#headlesstuss3)
