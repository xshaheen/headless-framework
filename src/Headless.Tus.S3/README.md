# Headless.Tus.S3

Amazon S3, Cloudflare R2, and S3-compatible TUS store implementation.

## Why use this package

Provides `TusS3Store`, an `ITusStore` implementation that backs resumable uploads with an S3 bucket through the application's own `IAmazonS3` client. It targets Cloudflare R2, Amazon S3, and S3-compatible servers such as MinIO. Supports Creation, CreationDeferLength, Concatenation, Expiration, and Termination; it does not support Checksum.

## Install

```bash
dotnet add package Headless.Tus.S3
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [TUS (Resumable Uploads) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/tus.md#headlesstuss3)
