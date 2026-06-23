# Headless.Imaging.Core

Orchestration layer that routes image processing calls to registered contributors.

## Problem Solved

Provides the `IImageResizer` and `IImageCompressor` implementations that dispatch to one or more backend contributors, buffers non-seekable streams transparently, and applies the configured default resize mode.

## Key Features

- `ImageResizer` — iterates `IImageResizerContributor` registrations (in reverse order) until one succeeds
- `ImageCompressor` — iterates `IImageCompressorContributor` registrations (in reverse order) until one succeeds
- `IImageResizerContributor` — contributor interface: `TryResizeAsync(Stream, ImageResizeArgs, CancellationToken)`
- `IImageCompressorContributor` — contributor interface: `TryCompressAsync(Stream, ImageCompressArgs, CancellationToken)`
- `ImagingOptions` — `DefaultResizeMode` applied when args carry `ImageResizeMode.Default`
- `AddImagingBuilder` — fluent builder returned by `AddImaging()` for chaining provider registrations
- Automatic MemoryStream buffering for non-seekable input streams
- Options validation via FluentValidation at startup

## Design Notes

Contributors are enumerated in reverse order of DI registration. This mirrors the last-in-wins overriding model: a contributor added after `AddImageSharpContributors()` takes precedence over ImageSharp without requiring any removal. A contributor signals non-support by returning `ImageProcessState.Unsupported`; the orchestrator seeks the stream back to the start and tries the next contributor.

## Installation

```bash
dotnet add package Headless.Imaging.Core
```

## Quick Start

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

## Configuration

`ImagingOptions` (one property):

| Property | Type | Default | Description |
|---|---|---|---|
| `DefaultResizeMode` | `ImageResizeMode` | `None` | Applied when `ImageResizeArgs.Mode` is `Default`. `None` means no resize fallback. |

## Dependencies

- `Headless.Imaging.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `IImageResizer` as singleton (`ImageResizer`)
- Registers `IImageCompressor` as singleton (`ImageCompressor`)
