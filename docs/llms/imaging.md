---
domain: Imaging
packages: Imaging.Abstractions, Imaging.Core, Imaging.ImageSharp
---

# Imaging

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Imaging.Abstractions](#headlessimagingabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Usage](#usage)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Imaging.Core](#headlessimagingcore)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration-1)
    - [Options](#options)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Imaging.ImageSharp](#headlessimagingimagesharp)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-1)
  - [Configuration](#configuration-2)
    - [Options](#options-1)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)

> Image resizing and compression pipeline with contributor-based extensibility, powered by ImageSharp.

## Quick Orientation

Install all three packages for a complete imaging pipeline:
- `Headless.Imaging.Abstractions` — interfaces (`IImageResizer`, `IImageCompressor`) and argument types
- `Headless.Imaging.Core` — orchestration layer, options, DI registration via `AddImaging()`
- `Headless.Imaging.ImageSharp` — actual processing via SixLabors.ImageSharp

Typical registration:
```csharp
builder.Services
    .AddImaging(options =>
    {
        options.DefaultQuality = 85;
        options.MaxWidth = 4096;
        options.MaxHeight = 4096;
    })
    .AddImageSharpContributors(options =>
    {
        options.JpegQuality = 85;
        options.PngCompressionLevel = 6;
        options.WebPQuality = 80;
    });
```

Supports JPEG, PNG, WebP, and GIF formats.

## Agent Instructions

- Always install all three packages (Abstractions + Core + ImageSharp) for a working pipeline. Abstractions alone provides no implementation.
- Inject `IImageResizer` for resizing, `IImageCompressor` for compression. Both are registered as singletons.
- Use `ImageResizeArgs` to specify `Width`, `Height`, and `Mode` (`ImageResizeMode.Max`, `Crop`, `Pad`, `Stretch`).
- Use `ImageCompressArgs` to specify `Quality` and target format.
- The contributor pattern (`IImageResizerContributor`, `IImageCompressorContributor`) allows adding custom processing backends alongside or instead of ImageSharp.
- Call `.AddImageSharpContributors()` on the builder returned by `.AddImaging()` — do not register ImageSharp contributors manually.
- Processing is stream-based. Always dispose returned streams when done.
- For custom processing steps, implement `IImageResizerContributor` or `IImageCompressorContributor` and register via DI.

---
# Headless.Imaging.Abstractions

Defines the unified interface for image processing operations.

## Problem Solved

Provides a provider-agnostic API for image resizing and compression, enabling seamless switching between image processing libraries without changing application code.

## Key Features

- `IImageResizer` - Interface for image resizing operations
- `IImageCompressor` - Interface for image compression operations
- `ImageResizeArgs` - Configuration for resize operations (dimensions, mode)
- `ImageCompressArgs` - Configuration for compression (quality, format)
- `ImageResizeMode` - Resize modes (Crop, Pad, Stretch, etc.)
- Result models with processing state information

## Installation

```bash
dotnet add package Headless.Imaging.Abstractions
```

## Usage

```csharp
public sealed class ImageService(IImageResizer resizer, IImageCompressor compressor)
{
    public async Task<Stream> ProcessAsync(Stream input, CancellationToken ct)
    {
        var resized = await resizer.ResizeAsync(input, new ImageResizeArgs
        {
            Width = 800,
            Height = 600,
            Mode = ImageResizeMode.Max
        }, ct).ConfigureAwait(false);

        return resized.Stream;
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
---
# Headless.Imaging.Core

Core image processing implementation with contributor-based extensibility.

## Problem Solved

Provides the orchestration layer for image processing, delegating to registered contributors (like ImageSharp) for actual processing while maintaining a unified API.

## Key Features

- `ImageResizer` - Orchestrates resize operations across contributors
- `ImageCompressor` - Orchestrates compression operations
- Contributor pattern for extensibility (`IImageResizerContributor`, `IImageCompressorContributor`)
- Builder pattern for fluent registration
- Options validation

## Installation

```bash
dotnet add package Headless.Imaging.Core
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddImaging(options =>
    {
        options.DefaultQuality = 85;
    })
    .AddImageSharpContributors(); // From Headless.Imaging.ImageSharp
```

## Configuration

### Options

```csharp
services.AddImaging(options =>
{
    options.DefaultQuality = 85;        // Default compression quality
    options.MaxWidth = 4096;            // Maximum allowed width
    options.MaxHeight = 4096;           // Maximum allowed height
});
```

## Dependencies

- `Headless.Imaging.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `IImageResizer` as singleton
- Registers `IImageCompressor` as singleton
---
# Headless.Imaging.ImageSharp

ImageSharp-based implementation for image resizing and compression.

## Problem Solved

Provides high-performance, cross-platform image processing using SixLabors.ImageSharp, supporting various formats and advanced resize/compression operations.

## Key Features

- `ImageSharpImageResizerContributor` - ImageSharp resize implementation
- `ImageSharpImageCompressorContributor` - ImageSharp compression implementation
- Support for JPEG, PNG, WebP, GIF formats
- Configurable encoder settings per format
- Memory-efficient processing

## Installation

```bash
dotnet add package Headless.Imaging.ImageSharp
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddImaging()
    .AddImageSharpContributors(options =>
    {
        options.JpegQuality = 85;
        options.PngCompressionLevel = 6;
    });
```

## Configuration

### Options

```csharp
services.AddImaging()
    .AddImageSharpContributors(options =>
    {
        options.JpegQuality = 85;           // JPEG quality (1-100)
        options.PngCompressionLevel = 6;    // PNG compression (0-9)
        options.WebPQuality = 80;           // WebP quality (1-100)
    });
```

## Dependencies

- `Headless.Imaging.Core`
- `SixLabors.ImageSharp`

## Side Effects

- Registers `IImageResizerContributor` as singleton
- Registers `IImageCompressorContributor` as singleton
