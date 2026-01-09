# Framework.Imaging.ImageSharp

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
dotnet add package Framework.Imaging.ImageSharp
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

- `Framework.Imaging.Core`
- `SixLabors.ImageSharp`

## Side Effects

- Registers `IImageResizerContributor` as singleton
- Registers `IImageCompressorContributor` as singleton
