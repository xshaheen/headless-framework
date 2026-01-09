# Framework.Imaging.Abstractions

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
dotnet add package Framework.Imaging.Abstractions
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
        }, ct).AnyContext();

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
