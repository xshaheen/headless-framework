---
domain: Imaging
packages: Imaging.Abstractions, Imaging.Core, Imaging.ImageSharp
---

# Imaging

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Abstraction layer](#abstraction-layer)
    - [Contributor pipeline](#contributor-pipeline)
    - [Result model](#result-model)
    - [Resize modes](#resize-modes)
- [Headless.Imaging.Abstractions](#headlessimagingabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Imaging.Core](#headlessimagingcore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Imaging.ImageSharp](#headlessimagingimagesharp)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> Image resizing and compression pipeline with contributor-based extensibility, powered by ImageSharp.

## Quick Orientation

Install all three packages for a complete imaging pipeline:

- `Headless.Imaging.Abstractions` — interfaces (`IImageResizer`, `IImageCompressor`) and argument/result types
- `Headless.Imaging.Core` — orchestration layer, `ImagingOptions`, DI registration via `AddImaging()`
- `Headless.Imaging.ImageSharp` — SixLabors.ImageSharp-backed contributor registered by `AddImageSharpContributors()`

Typical registration:

```csharp
builder
    .Services.AddImaging(options =>
    {
        options.DefaultResizeMode = ImageResizeMode.Max;
    })
    .AddImageSharpContributors(options =>
    {
        options.DefaultCompressQuality = 80;
    });
```

Supports JPEG, PNG, WebP, and GIF formats for resize; JPEG, PNG, and WebP for compression.

## Agent Instructions

- Always install all three packages (Abstractions + Core + ImageSharp) for a working pipeline. Abstractions alone provides no implementation; Core alone has no image-processing backend.
- Inject `IImageResizer` for resizing, `IImageCompressor` for compression. Both are registered as singletons.
- `ImageResizeArgs` is constructed via its constructors — it has no settable `Width`/`Height`/`Mode` object-initializer properties. Pick the right overload: `(ImageResizeMode, int width, int? height, string? mimeType)`, `(ImageResizeMode, int? width, int height, string? mimeType)`, or `(ImageResizeMode, int width, int height, string? mimeType)`. Negative values throw `ArgumentException` at construction time.
- Check `result.IsDone` before accessing `result.Result`. When `IsDone` is `false`, `result.Error` is non-null and `result.Result` is null. This is enforced by `[MemberNotNullWhen]` attributes.
- For `ImageStreamResizeResult`: access the processed image via `result.Result.Content` (a `Stream`), not `.Stream`. The result also carries `result.Result.MimeType`, `result.Result.Width`, and `result.Result.Height`.
- `ImageCompressArgs` accepts only an optional `mimeType`. It carries no quality setting — quality is governed by `ImageSharpOptions` encoders.
- `ImageResizeMode.Default` is resolved at runtime to `ImagingOptions.DefaultResizeMode`. If `DefaultResizeMode` is also `None` (the default), the image is returned unchanged.
- `ImageResizeMode.None` skips resizing entirely and returns the original stream. Use `Max`, `Crop`, `Pad`, `BoxPad`, `Min`, or `Stretch` for actual resizing.
- The compressor returns `ImageProcessState.Failed` when the compressed output is larger than the original — it never bloats a file. Check `result.IsDone` to detect this case.
- Contributors are iterated in **reverse registration order**. The last-registered contributor is tried first. If you add a custom contributor after `AddImageSharpContributors()`, it takes priority over ImageSharp.
- Non-seekable input streams are automatically buffered into a `MemoryStream` by both `ImageResizer` and `ImageCompressor`. Callers do not need to buffer first.
- Returned `Stream` objects in results are owned by the caller. Dispose them when done.
- GIF and BMP are supported for resize but not for compression. Passing a GIF/BMP stream to `IImageCompressor` returns `ImageProcessState.Unsupported`.
- Call `.AddImageSharpContributors()` on the `AddImagingBuilder` returned by `.AddImaging()`. Do not register `ImageSharpImageResizerContributor` or `ImageSharpImageCompressorContributor` manually.

---

## Core Concepts

### Abstraction layer

`IImageResizer` and `IImageCompressor` are the only types application code should reference. Both accept a `Stream` and typed args, and return a result carrying either a processed `Stream` or an error.

### Contributor pipeline

`Headless.Imaging.Core` does not process images itself. It iterates a registered list of `IImageResizerContributor` / `IImageCompressorContributor` instances in reverse order and returns the first non-`Unsupported` result. A contributor returns `ImageProcessState.Unsupported` to signal that it does not handle the given format; the orchestrator then tries the next contributor. This design allows multiple processing backends to coexist.

### Result model

All results derive from `ImageProcessResult<T>` with three states:

| `ImageProcessState` | Meaning |
|---|---|
| `Done` | Processing succeeded. `Result` is non-null; `Error` is null. |
| `Unsupported` | No contributor could handle the format. `Error` describes why. |
| `Failed` | A contributor attempted processing but it failed (e.g., compressed output was larger). |

`IsDone` is a `[MemberNotNullWhen]`-annotated shorthand: `true` iff `State == Done`.

### Resize modes

`ImageResizeMode` maps to SixLabors.ImageSharp resize modes:

| Mode | Behavior |
|---|---|
| `None` | No resize — original stream returned as-is. |
| `Default` | Resolved at runtime to `ImagingOptions.DefaultResizeMode`. |
| `Max` | Scale down to fit within width × height, preserving aspect ratio. Never upscales. |
| `Crop` | Resize and crop to exact width × height. |
| `Pad` | Resize to fit, pad remaining area. Accepts a single dimension. |
| `BoxPad` | Pad without resizing source; downscale behaves like `Pad`. |
| `Min` | Scale until the shortest side reaches the target. Never upscales. |
| `Stretch` | Stretch to exact width × height, ignoring aspect ratio. |

---

## Headless.Imaging.Abstractions

Defines the provider-agnostic contracts for image processing operations.

### Problem Solved

Decouples application code from any specific image-processing library. Services that inject `IImageResizer` or `IImageCompressor` have no compile-time dependency on SixLabors.ImageSharp or any other backend.

### Key Features

- `IImageResizer` — resize interface: `ResizeAsync(Stream, ImageResizeArgs, CancellationToken)`
- `IImageCompressor` — compression interface: `CompressAsync(Stream, ImageCompressArgs, CancellationToken)`
- `ImageResizeArgs` — resize parameters: mode, width, height, optional MIME type override
- `ImageCompressArgs` — compression parameters: optional MIME type override
- `ImageResizeMode` — enum of resize strategies (`None`, `Default`, `Max`, `Crop`, `Pad`, `BoxPad`, `Min`, `Stretch`)
- `ImageStreamResizeResult` / `ImageStreamCompressResult` — typed result wrappers
- `ImageProcessResult<T>` — base result with `IsDone`, `State`, `Result`, `Error`
- `ImageProcessState` — `Done`, `Unsupported`, `Failed`
- `ImageResizeContent<TContent>` — carries `Content`, `MimeType`, `Width`, `Height` for resize results

### Installation

```bash
dotnet add package Headless.Imaging.Abstractions
```

### Quick Start

```csharp
public sealed class ImageService(IImageResizer resizer, IImageCompressor compressor)
{
    public async Task<Stream?> ResizeAsync(Stream input, CancellationToken ct)
    {
        var result = await resizer.ResizeAsync(
            input,
            new ImageResizeArgs(ImageResizeMode.Max, width: 800, height: 600),
            ct
        );

        if (!result.IsDone)
        {
            // result.Error is non-null here (Unsupported or Failed)
            return null;
        }

        // result.Result.Content is the resized stream (caller must dispose)
        return result.Result.Content;
    }

    public async Task<Stream?> CompressAsync(Stream input, CancellationToken ct)
    {
        var result = await compressor.CompressAsync(input, new ImageCompressArgs(), ct);

        return result.IsDone ? result.Result : null;
    }
}
```

### Configuration

None. This package defines only interfaces and data types — no DI registration, no options.

### Dependencies

None.

### Side Effects

None.

---

## Headless.Imaging.Core

Orchestration layer that routes image processing calls to registered contributors.

### Problem Solved

Provides the `IImageResizer` and `IImageCompressor` implementations that dispatch to one or more backend contributors, buffers non-seekable streams transparently, and applies the configured default resize mode.

### Key Features

- `ImageResizer` — iterates `IImageResizerContributor` registrations (in reverse order) until one succeeds
- `ImageCompressor` — iterates `IImageCompressorContributor` registrations (in reverse order) until one succeeds
- `IImageResizerContributor` — contributor interface: `TryResizeAsync(Stream, ImageResizeArgs, CancellationToken)`
- `IImageCompressorContributor` — contributor interface: `TryCompressAsync(Stream, ImageCompressArgs, CancellationToken)`
- `ImagingOptions` — `DefaultResizeMode` applied when args carry `ImageResizeMode.Default`
- `AddImagingBuilder` — fluent builder returned by `AddImaging()` for chaining provider registrations
- Automatic MemoryStream buffering for non-seekable input streams
- Options validation via FluentValidation at startup

### Design Notes

Contributors are enumerated in reverse order of DI registration. This mirrors the last-in-wins overriding model: a contributor added after `AddImageSharpContributors()` takes precedence over ImageSharp without requiring any removal. A contributor signals non-support by returning `ImageProcessState.Unsupported`; the orchestrator seeks the stream back to the start and tries the next contributor.

### Installation

```bash
dotnet add package Headless.Imaging.Core
```

### Quick Start

```csharp
builder
    .Services.AddImaging(options =>
    {
        options.DefaultResizeMode = ImageResizeMode.Max;
    })
    .AddImageSharpContributors(); // from Headless.Imaging.ImageSharp
```

Three `AddImaging` overloads are available:

```csharp
// Bind from IConfiguration section
services.AddImaging(config.GetSection("Headless:Imaging"));

// Configure with action
services.AddImaging(options => options.DefaultResizeMode = ImageResizeMode.Crop);

// Configure with action + IServiceProvider
services.AddImaging((options, sp) => options.DefaultResizeMode = ImageResizeMode.Max);
```

### Configuration

`ImagingOptions` (one property):

| Property | Type | Default | Description |
|---|---|---|---|
| `DefaultResizeMode` | `ImageResizeMode` | `None` | Applied when `ImageResizeArgs.Mode` is `Default`. `None` means no resize fallback. |

### Dependencies

- `Headless.Imaging.Abstractions`
- `Headless.Hosting`

### Side Effects

- Registers `IImageResizer` as singleton (`ImageResizer`)
- Registers `IImageCompressor` as singleton (`ImageCompressor`)

---

## Headless.Imaging.ImageSharp

SixLabors.ImageSharp-backed contributors for image resizing and compression.

### Problem Solved

Provides the actual image-processing implementation wired into the contributor pipeline. Supports JPEG, PNG, WebP, GIF, BMP, and TIFF for resize; JPEG, PNG, and WebP for compression.

### Key Features

- `ImageSharpImageResizerContributor` — resize via `SixLabors.ImageSharp`; supports JPEG, PNG, GIF, BMP, TIFF, WebP
- `ImageSharpImageCompressorContributor` — compression via configurable `IImageEncoder` per format; supports JPEG, PNG, WebP
- `ImageSharpOptions` — encoder settings with per-format encoder instances
- Compression skips output if compressed size exceeds original (returns `Failed`)
- Format is auto-detected from stream metadata when `args.MimeType` is not provided

### Design Notes

`ImageSharpOptions` exposes full `IImageEncoder` instances (`JpegCompressEncoder`, `PngCompressEncoder`, `WebpCompressEncoder`) rather than simple quality integers. This gives callers full control over encoder-specific settings (chroma subsampling, interlacing, filter type, etc.). The `DefaultCompressQuality` property initializes the default JPEG and WebP encoders in the constructor; changing it after construction has no effect unless the encoder instances are also replaced.

### Installation

```bash
dotnet add package Headless.Imaging.ImageSharp
```

### Quick Start

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
.AddImageSharpContributors(options =>
{
    options.JpegCompressEncoder = new JpegEncoder { Quality = 90 };
    options.PngCompressEncoder  = new PngEncoder  { CompressionLevel = PngCompressionLevel.BestCompression, SkipMetadata = true };
});
```

Three `AddImageSharpContributors` overloads are available (mirrors `AddImaging`):

```csharp
// Bind from IConfiguration section
builder.AddImageSharpContributors(config.GetSection("Headless:ImageSharp"));

// Configure with action
builder.AddImageSharpContributors(options => options.DefaultCompressQuality = 85);

// Configure with action + IServiceProvider
builder.AddImageSharpContributors((options, sp) => options.DefaultCompressQuality = 85);
```

### Configuration

`ImageSharpOptions`:

| Property | Type | Default | Description |
|---|---|---|---|
| `DefaultCompressQuality` | `int` (1–100) | `75` | Quality used to initialize the default JPEG and WebP encoders. Must be 1–100. |
| `JpegCompressEncoder` | `IImageEncoder` | `JpegEncoder { Quality = 75 }` | Encoder used for JPEG compression. Replace to control quality and chroma subsampling. |
| `PngCompressEncoder` | `IImageEncoder` | `PngEncoder { CompressionLevel = BestCompression, SkipMetadata = true }` | Encoder used for PNG compression. |
| `WebpCompressEncoder` | `IImageEncoder` | `WebpEncoder { Quality = 75 }` | Encoder used for WebP compression. |

Validation (applied at startup): `DefaultCompressQuality` must be between 1 and 100; all three encoder properties must be non-null.

### Dependencies

- `Headless.Imaging.Core`
- `SixLabors.ImageSharp`

### Side Effects

- Registers `IImageResizerContributor` as singleton (`ImageSharpImageResizerContributor`)
- Registers `IImageCompressorContributor` as singleton (`ImageSharpImageCompressorContributor`)
