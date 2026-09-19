# Headless.Imaging.Abstractions

Defines the provider-agnostic contracts for image processing operations.

## Why use this package

Decouples application code from any specific image-processing library. Services that inject `IImageResizer` or `IImageCompressor` have no compile-time dependency on SixLabors.ImageSharp or any other backend.

## Install

```bash
dotnet add package Headless.Imaging.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Imaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/imaging.md#headlessimagingabstractions)
