# Headless.Imaging.ImageSharp

SixLabors.ImageSharp-backed contributors for image resizing and compression.

## Problem Solved

Provides the actual image-processing implementation wired into the contributor pipeline. Supports JPEG, PNG, WebP, GIF, BMP, and TIFF for resize; JPEG, PNG, and WebP for compression.

## Key Features

- Internal ImageSharp-backed `IImageResizerContributor` (registered by `AddImageSharpContributors`) — resize via `SixLabors.ImageSharp`; supports JPEG, PNG, GIF, BMP, TIFF, WebP
- Internal ImageSharp-backed `IImageCompressorContributor` (registered by `AddImageSharpContributors`) — compression via configurable `IImageEncoder` per format; supports JPEG, PNG, WebP
- `ImageSharpOptions` — encoder settings with per-format encoder instances
- Compression skips output if compressed size exceeds original (returns `Failed`)
- Format is auto-detected from stream metadata when `args.MimeType` is not provided

## Design Notes

`ImageSharpOptions` exposes full `IImageEncoder` instances (`JpegCompressEncoder`, `PngCompressEncoder`, `WebpCompressEncoder`) rather than simple quality integers. This gives callers full control over encoder-specific settings (chroma subsampling, interlacing, filter type, etc.). The `DefaultCompressQuality` property initializes the default JPEG and WebP encoders in the constructor; changing it after construction has no effect unless the encoder instances are also replaced.

## Installation

```bash
dotnet add package Headless.Imaging.ImageSharp
```

## Quick Start

```csharp
builder
    .Services.AddImaging()
    .AddImageSharpContributors(options =>
    {
        options.DefaultCompressQuality = 80; // sets JPEG + WebP encoder quality
    });
```

To override a specific encoder:

```csharp
builder
    .Services.AddImaging()
    .AddImageSharpContributors(options =>
    {
        options.JpegCompressEncoder = new JpegEncoder { Quality = 90 };
        options.PngCompressEncoder = new PngEncoder
        {
            CompressionLevel = PngCompressionLevel.BestCompression,
            SkipMetadata = true,
        };
    });
```

## Configuration

`ImageSharpOptions`:

| Property | Type | Default | Description |
|---|---|---|---|
| `DefaultCompressQuality` | `int` (1–100) | `75` | Quality used to initialize the default JPEG and WebP encoders. Must be 1–100. |
| `JpegCompressEncoder` | `IImageEncoder` | `JpegEncoder { Quality = 75 }` | Encoder used for JPEG compression. Replace to control quality and chroma subsampling. |
| `PngCompressEncoder` | `IImageEncoder` | `PngEncoder { CompressionLevel = BestCompression, SkipMetadata = true }` | Encoder used for PNG compression. |
| `WebpCompressEncoder` | `IImageEncoder` | `WebpEncoder { Quality = 75 }` | Encoder used for WebP compression. |

Validation (applied at startup): `DefaultCompressQuality` must be between 1 and 100; all three encoder properties must be non-null.

## Dependencies

- `Headless.Imaging.Core`
- `SixLabors.ImageSharp`

## Side Effects

- Registers `IImageResizerContributor` as singleton (internal ImageSharp resize contributor)
- Registers `IImageCompressorContributor` as singleton (internal ImageSharp compress contributor)
