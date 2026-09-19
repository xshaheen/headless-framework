# Headless.Blobs.Redis

Redis implementation of `IBlobStorage` for storing small, ephemeral blobs in Redis.

## Why use this package

Provides high-speed blob storage for small files using Redis, for temporary files, cache data, or session-related binary content. Not a general-purpose store — the 10 MB default limit and Redis memory model make it unsuitable for large files.

## Install

```bash
dotnet add package Headless.Blobs.Redis
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Blob Storage guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/blobs.md#headlessblobsredis)
