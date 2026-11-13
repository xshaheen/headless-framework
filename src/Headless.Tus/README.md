# Headless.Tus

Root package of the Headless TUS (resumable upload) stack: the `AddHeadlessTus` registration, upload endpoints over `tusdotnet` that isolate, validate, and hand off each upload, CORS defaults for browser clients, and the expired-uploads cleanup job.

## Why use this package

`tusdotnet` serves the tus protocol and a store keeps the bytes. An application that accepts uploads from signed-in users still needs four things on top:

- **Isolation.** Each upload is bound to the endpoint, owner, and tenant that created it, and is "not found" for anyone else.
- **Validation.** A size limit, content-type and extension allow-lists checked against the upload's real first bytes, required metadata, and the application's own checks.
- **A completion handler.** The application's handler receives each finished upload, and receives it again when it failed.
- **Housekeeping.** Expired incomplete uploads are removed, and browsers get the CORS headers that tus clients need to read `Location` and `Upload-Offset`.

Pair it with one store package (`Headless.Tus.Azure` or `Headless.Tus.S3`) or any other `ITusStore`, and add `Headless.Tus.DistributedLocks` when more than one node serves uploads.

## Install

```bash
dotnet add package Headless.Tus
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [TUS (Resumable Uploads) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/tus.md#headlesstus)
