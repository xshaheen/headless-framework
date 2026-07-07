# Headless.Imaging.Abstractions

Defines the provider-agnostic contracts for image processing operations.

## Problem Solved

Decouples application code from any specific image-processing library. Services that inject `IImageResizer` or `IImageCompressor` have no compile-time dependency on SixLabors.ImageSharp or any other backend.

## Key Features

- `IImageResizer` — resize interface: `ResizeAsync(Stream, ImageResizeArgs, CancellationToken)`
- `IImageCompressor` — compression interface: `CompressAsync(Stream, ImageCompressArgs, CancellationToken)`
- `ImageResizeArgs` — resize parameters: mode, width, height, optional MIME type override
- `ImageCompressArgs` — compression parameters: optional MIME type override
- `ImageResizeMode` — enum of resize strategies (`Default` (zero/unset sentinel resolved to `ImagingOptions.DefaultResizeMode`), `None`, `Max`, `Crop`, `Pad`, `BoxPad`, `Min`, `Stretch`)
- `ImageStreamResizeResult` / `ImageStreamCompressResult` — typed result wrappers
- `ImageProcessResult<T>` — base result with `IsDone`, `State`, `Result`, `Error`
- `ImageProcessState` — `Done`, `Unsupported`, `Failed`
- `ImageResizeContent<TContent>` — carries `Content`, `MimeType`, `Width`, `Height` for resize results

## Installation

```bash
dotnet add package Headless.Imaging.Abstractions
```

## Quick Start

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

## Configuration

None. This package defines only interfaces and data types — no DI registration, no options.

## Dependencies

None.

## Side Effects

None.
