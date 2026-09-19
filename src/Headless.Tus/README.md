# Headless.Tus

Base package for the Headless TUS (resumable upload) stack: the shared `tusdotnet` dependency plus
the protocol-level pieces every tus deployment needs regardless of storage provider — CORS defaults
for browser clients and the expired-uploads cleanup job.

## Why use this package

Two gaps every tus deployment hits regardless of which store it uses:

- **Browsers hide tus response headers cross-origin.** Without the right
  `Access-Control-Expose-Headers`, clients like `tus-js-client` and Uppy cannot read
  `Location`/`Upload-Offset`, so every cross-origin upload fails on the first request. The exact
  header list is protocol lore that otherwise gets copy-pasted from blog posts.
- **Nothing removes expired uploads.** tusdotnet only reports `Upload-Expires`; unfinished uploads
  accumulate forever unless the application runs a cleanup job.

The package also pins the shared `tusdotnet` + `Headless.Hosting` references so every TUS provider
package aligns on one version.

## Install

```bash
dotnet add package Headless.Tus
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [TUS (Resumable Uploads) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/tus.md#headlesstus)
