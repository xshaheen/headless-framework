# Headless.Imaging.Core

Orchestration layer that routes image processing calls to registered contributors.

## Why use this package

Provides the `IImageResizer` and `IImageCompressor` implementations that dispatch to one or more backend contributors, buffers non-seekable streams transparently, and applies the configured default resize mode.

## Install

```bash
dotnet add package Headless.Imaging.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Imaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/imaging.md#headlessimagingcore)
