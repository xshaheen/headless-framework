# Headless.Blobs.SignedUrlEndpoint

Signed download and upload URLs for Headless blob stores that have no native presign, served by an ASP.NET Core endpoint.

## Why use this package

FileSystem, Redis, and SFTP stores cannot mint presigned URLs themselves. This package gives them `IPresignedUrlBlobStorage` through a data-protection-signed endpoint that streams the bytes through the application, so code that hands out download or upload URLs works unchanged from local FileSystem development to S3 or Azure in production.

## Install

```bash
dotnet add package Headless.Blobs.SignedUrlEndpoint
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Blob Storage guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/blobs.md#headlessblobssignedurlendpoint)
