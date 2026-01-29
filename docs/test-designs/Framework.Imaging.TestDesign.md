# Test Case Design: Headless.Imaging (All Packages)

**Packages:**
- `src/Headless.Imaging.Abstractions`
- `src/Headless.Imaging.Core`
- `src/Headless.Imaging.ImageSharp`

**Test Projects:** None (new projects needed)
**Generated:** 2026-01-25

## Package Analysis

### Framework.Imaging.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `IImageResizer.cs` | Image resizer interface | Low (interface) |
| `IImageCompressor.cs` | Image compressor interface | Low (interface) |
| `Contracts/ImageResizeArgs.cs` | Resize arguments | Medium |
| `Contracts/ImageCompressArgs.cs` | Compress arguments | Medium |
| `Contracts/ImageResizeMode.cs` | Resize mode enum | Low (enum) |
| `Contracts/ImageResizeResult.cs` | Resize result | Medium |
| `Contracts/ImageCompressResult.cs` | Compress result | Medium |
| `Contracts/ImageProcessState.cs` | Processing state enum | Low (enum) |
| `Contracts/ImageProcessResult.cs` | Base result class | Medium |

### Framework.Imaging.Core

| File | Purpose | Testable |
|------|---------|----------|
| `ImageResizer.cs` | IImageResizer impl with contributor chain | High |
| `ImageCompressor.cs` | IImageCompressor impl with contributor chain | High |
| `IImageResizerContributor.cs` | Resizer contributor interface | Low (interface) |
| `IImageCompressorContributor.cs` | Compressor contributor interface | Low (interface) |
| `ImagingOptions.cs` | Imaging options | Medium |
| `AddImagingBuilder.cs` | DI builder | Low |
| `Setup.cs` | DI registration | Low |

### Framework.Imaging.ImageSharp

| File | Purpose | Testable |
|------|---------|----------|
| `ImageSharpImageResizerContributor.cs` | ImageSharp resizer | High (integration) |
| `ImageSharpImageCompressorContributor.cs` | ImageSharp compressor | High (integration) |
| `ImageSharpOptions.cs` | ImageSharp configuration | Medium |
| `Internals/LoadImageHelpers.cs` | Image loading helpers | Medium |
| `Setup.cs` | DI registration | Low |

## Current Test Coverage

**Existing Tests:** None

---

## Missing: ImageResizer Tests

**File:** `tests/Headless.Imaging.Tests.Unit/ImageResizerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_stream_is_null` | Argument validation |
| `should_return_cannot_read_when_stream_not_readable` | CanRead check |
| `should_copy_to_memory_stream_when_not_seekable` | CanSeek handling |
| `should_try_contributors_in_reverse_order` | Last registered first |
| `should_return_first_supported_result` | Chain processing |
| `should_return_not_supported_when_no_contributor_handles` | Empty chain |
| `should_seek_stream_to_begin_after_each_contributor` | Stream position reset |
| `should_use_default_resize_mode_from_options` | ImageResizeMode.Default |
| `should_preserve_custom_resize_mode` | Non-default mode preserved |

---

## Missing: ImageCompressor Tests

**File:** `tests/Headless.Imaging.Tests.Unit/ImageCompressorTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_stream_is_null` | Argument validation |
| `should_return_cannot_read_when_stream_not_readable` | CanRead check |
| `should_copy_to_memory_stream_when_not_seekable` | CanSeek handling |
| `should_try_contributors_in_reverse_order` | Last registered first |
| `should_return_first_supported_result` | Chain processing |
| `should_return_not_supported_when_no_contributor_handles` | Empty chain |
| `should_seek_stream_to_begin_after_each_contributor` | Stream position reset |

---

## Missing: ImageProcessState Tests

**File:** `tests/Headless.Imaging.Tests.Unit/Contracts/ImageProcessResultTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_create_success_result` | State = Success |
| `should_create_not_supported_result` | State = Unsupported |
| `should_create_cannot_read_result` | State = CannotRead |
| `should_include_stream_in_success` | Output stream |

---

## Missing: ImageSharpImageResizerContributor Tests (Integration)

**File:** `tests/Headless.Imaging.ImageSharp.Tests.Integration/ImageSharpImageResizerContributorTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_resize_jpeg_image` | JPEG support |
| `should_resize_png_image` | PNG support |
| `should_resize_to_specified_dimensions` | Width/Height |
| `should_maintain_aspect_ratio` | Ratio preservation |
| `should_use_max_resize_mode` | Max mode |
| `should_use_crop_resize_mode` | Crop mode |
| `should_use_pad_resize_mode` | Pad mode |
| `should_return_unsupported_for_invalid_image` | Invalid input |

---

## Missing: ImageSharpImageCompressorContributor Tests (Integration)

**File:** `tests/Headless.Imaging.ImageSharp.Tests.Integration/ImageSharpImageCompressorContributorTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_compress_jpeg_image` | JPEG compression |
| `should_compress_png_image` | PNG compression |
| `should_respect_quality_setting` | Quality parameter |
| `should_reduce_file_size` | Size reduction |
| `should_return_unsupported_for_invalid_image` | Invalid input |

---

## Test Infrastructure

### Mock Contributors

```csharp
public sealed class FakeImageResizerContributor : IImageResizerContributor
{
    public ImageProcessState ResultState { get; set; } = ImageProcessState.Success;
    public MemoryStream? OutputStream { get; set; }
    public int CallCount { get; private set; }

    public Task<ImageStreamResizeResult> TryResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (ResultState == ImageProcessState.Unsupported)
            return Task.FromResult(ImageStreamResizeResult.NotSupported());

        return Task.FromResult(new ImageStreamResizeResult(ResultState, OutputStream ?? new MemoryStream()));
    }
}
```

---

## Test Summary

| Component | Existing | New Unit | New Integration | Total |
|-----------|----------|----------|-----------------|-------|
| ImageResizer | 0 | 9 | 0 | 9 |
| ImageCompressor | 0 | 7 | 0 | 7 |
| ImageProcessResult | 0 | 4 | 0 | 4 |
| ImageSharpResizer | 0 | 0 | 8 | 8 |
| ImageSharpCompressor | 0 | 0 | 5 | 5 |
| **Total** | **0** | **20** | **13** | **33** |

---

## Priority Order

1. **ImageResizer** - Core resize orchestration
2. **ImageCompressor** - Core compress orchestration
3. **ImageSharp contributors** - Actual image processing

---

## Notes

1. **Contributor pattern** - Last registered contributor is tried first (reversed)
2. **Stream handling** - Auto-copies to MemoryStream if not seekable
3. **Result states** - Success, Unsupported, CannotRead
4. **Default resize mode** - Configurable via ImagingOptions.DefaultResizeMode
5. **ImageSharp** - Uses SixLabors.ImageSharp library

---

## Imaging Architecture

```
IImageResizer
├── ResizeAsync(Stream, ImageResizeArgs)
└── Returns ImageStreamResizeResult

ImageResizer (Core impl)
├── contributors.Reverse() (last registered first)
├── For each contributor:
│   ├── TryResizeAsync()
│   ├── Seek stream to begin
│   └── Return if not Unsupported
└── Return NotSupported if no handler

Contributor Chain:
└── ImageSharpImageResizerContributor (default)

IImageCompressor (same pattern)
└── ImageSharpImageCompressorContributor
```

---

## Recommendation

**Medium Priority** - Image processing is commonly used. Unit tests for:
- Resizer/Compressor chain logic
- Stream handling edge cases
- Result state factory methods

Integration tests require real image files and ImageSharp.
