# Headless.Blobs.CloudflareR2

Cloudflare R2 implementation of `IBlobStorage`, running R2 as a private, S3-compatible blob backend on the reused AWS S3 engine.

## Why use this package

R2 speaks the S3 API but cannot use the AWS provider as-is: the endpoint, path-style addressing, and AWS SDK v4 checksum defaults need R2-specific configuration, and R2 has no ACL concept. This package configures an R2-tuned `IAmazonS3` via `R2ClientFactory` and reuses `AwsBlobStorage`, making R2 a drop-in, cost-saving S3 replacement.

## Install

```bash
dotnet add package Headless.Blobs.CloudflareR2
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Blob Storage guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/blobs.md#headlessblobscloudflarer2)
