---
domain: Imaging
packages: Imaging.Abstractions, Imaging, Imaging.NetVips
---

# Imaging

> Resizes, compresses, converts, and inspects images through libvips (NetVips), behind a contributor pipeline you can extend.

## Orientation

Install three packages and one libvips source:

- `Headless.Imaging.Abstractions`: `IImageResizer`, `IImageCompressor`, `IImageInspector`, and their argument and result types.
- `Headless.Imaging`: the pipeline that routes calls to contributors, `ImagingOptions`, and `AddHeadlessImaging(...)`.
- `Headless.Imaging.NetVips`: the libvips contributors, chosen with `imaging.UseNetVips(...)`, and `NetVipsOptions`.
- libvips itself: the `NetVips.Native` package, or libvips 8.15 or later on the host. See [Choose a libvips source](#choose-a-libvips-source).

All three packages use the `Headless.Imaging` namespace.

| Format | Inspect | Resize and convert | Compress into |
| --- | --- | --- | --- |
| JPEG, PNG, WebP, AVIF | Yes | Yes | Yes |
| GIF (animated included), TIFF | Yes | Yes | No. Compress them only into JPEG, PNG, WebP, or AVIF. |
| BMP, SVG, PDF, HEIC, JPEG XL | `Unsupported` | `Unsupported` | `Unsupported` |

## Agent Rules

- Register imaging once: `services.AddHeadlessImaging(imaging => imaging.UseNetVips())`. `AddHeadlessImaging` throws `InvalidOperationException` when no provider is chosen, and when it is called a second time on the same service collection. Put every `Configure` and `Use…` call in that one call.
- Add a libvips source. Without `NetVips.Native` or a system libvips 8.15 or later, the first resolution of `IImageResizer`, `IImageCompressor`, or `IImageInspector` throws `InvalidOperationException` naming both fixes.
- Inject `IImageResizer`, `IImageCompressor`, and `IImageInspector`, all singletons. The NetVips contributors are internal. Register them only through `UseNetVips()`.
- Check `result.IsDone` before you read `result.Result`. When `IsDone` is `false`, `Error` is non-null and `Result` is `null`.
- Read the resized image from `result.Result.Content`. A resize result also carries `MimeType` (the format actually written), `Width`, and `Height`. A compress result's `Result` is the `Stream` itself.
- Dispose every returned `Stream`. The caller owns it.
- Build `ImageResizeArgs` through a constructor: `(ImageResizeMode, int width, int? height = null, string? mimeType = null)`, `(ImageResizeMode, int? width, int height, string? mimeType = null)`, or `(ImageResizeMode, int width, int height, string? mimeType = null)`. `Width` and `Height` have no public setter. A zero or negative size throws `ArgumentException` at construction. `Mode` is settable.
- Set `OutputMimeType` to change the format: `new ImageResizeArgs(ImageResizeMode.Max, 800, 600) { OutputMimeType = ContentTypes.Images.Webp }`. Without it, the output keeps the source format. The `mimeType` argument is only a hint about the input. A contributor skips a hinted type it does not handle and otherwise detects the format from the bytes.
- Set quality in `NetVipsOptions` (`JpegQuality`, `WebpQuality`, `AvifQuality`, `PngCompressionLevel`). `ImageCompressArgs` has no quality setting.
- Expect `ImageProcessState.Failed` from `CompressAsync` when the compressed output is not smaller than the input. The compressor never returns a larger file.
- Validate an upload with `IImageInspector`, not its claimed content type. The inspector reads the format from the bytes.
- Pass a seekable stream when one upload goes through two calls, such as inspect then resize. Each call buffers a non-seekable stream only for itself. `IFormFile.OpenReadStream()` is seekable.
- Cancel through the token. A running libvips decode or encode stops within a few milliseconds and throws `OperationCanceledException`.

## Core Concepts

### Contributor pipeline

`IImageResizer`, `IImageCompressor`, and `IImageInspector` each try their registered contributors (`IImageResizerContributor`, `IImageCompressorContributor`, `IImageInspectorContributor`) in reverse registration order and return the first result that is not `Unsupported`. The stream is rewound before each attempt. A contributor you register after `AddHeadlessImaging(...)` is tried before the NetVips contributors, with no need to remove them.

### Result model

Every result derives from `ImageProcessResult<T>`:

| `ImageProcessState` | Meaning |
| --- | --- |
| `Done` | Processing succeeded. `Result` is non-null and `Error` is `null`. |
| `Unsupported` | No contributor handles the input. `Error` says why. The next contributor is tried. |
| `Failed` | A contributor took the input and could not finish, for example because the compressed output was larger or the image is over `MaxPixels`. No other contributor is tried. |

`IsDone` is `true` only when `State` is `Done`. It carries `[MemberNotNullWhen]`, so the compiler knows `Result` is non-null after the check.

### Resize modes

When you give only one side, the other side follows the source aspect ratio before the mode applies.

| `ImageResizeMode` | Behavior |
| --- | --- |
| `Default` | The unset value. Replaced at run time by `ImagingOptions.DefaultResizeMode`. If that is `None` (its default), the image is not resized. |
| `None` | No resize. Without an `OutputMimeType`, returns the source bytes. With one, converts at the source size. |
| `Max` | Scales down to fit inside width × height and keeps the aspect ratio. Never upscales. |
| `Crop` | Fills width × height exactly and crops the rest. `NetVipsOptions.CropFocus` picks the kept region. |
| `Pad` | Fits inside width × height, then pads to the exact size. Padding is transparent when the format has alpha, and white for JPEG. |
| `BoxPad` | Pads to width × height without resizing a source that fits. A larger source behaves like `Pad`. |
| `Min` | Puts the side whose target is nearest its source length exactly on the target and scales the other side with the aspect ratio. No crop. Never upscales: a source smaller than the box on either side keeps its size. |
| `Stretch` | Stretches to width × height exactly and ignores the aspect ratio. |

---

## Headless.Imaging.Abstractions

The contracts application code depends on: `IImageResizer.ResizeAsync(Stream, ImageResizeArgs, CancellationToken)`, `IImageCompressor.CompressAsync(Stream, ImageCompressArgs, CancellationToken)`, and `IImageInspector.InspectAsync(Stream, CancellationToken)`. It has no registration and no options.

`IImageInspector` returns an `ImageInfo`: `MimeType`, `Width` and `Height` as displayed (EXIF orientation applied, one frame of an animation), `FrameCount`, `HasAlpha`, and `IsAnimated`.

```csharp
public sealed class ImageService(IImageResizer resizer, IImageCompressor compressor)
{
    public async Task<Stream?> ResizeAsync(Stream input, CancellationToken ct)
    {
        var result = await resizer.ResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, width: 800, height: 600), ct);

        // Unsupported or Failed: result.Error says why.
        return result.IsDone ? result.Result.Content : null;
    }

    public async Task<Stream?> CompressAsync(Stream input, CancellationToken ct)
    {
        var result = await compressor.CompressAsync(input, new ImageCompressArgs(), ct);

        return result.IsDone ? result.Result : null;
    }
}
```

Validate an upload by its bytes, then store a WebP thumbnail. The inspector and the resizer each read the stream from the start, so pass a seekable stream:

```csharp
public sealed class AvatarService(IImageInspector inspector, IImageResizer resizer)
{
    public async Task<Stream?> AcceptAsync(Stream upload, CancellationToken ct)
    {
        var inspection = await inspector.InspectAsync(upload, ct);

        // Unsupported for SVG, BMP, HEIC, or anything that is not an image, whatever its file name says.
        if (!inspection.IsDone || inspection.Result.Width < 64 || inspection.Result.IsAnimated)
        {
            return null;
        }

        var resized = await resizer.ResizeAsync(
            upload,
            new ImageResizeArgs(ImageResizeMode.Crop, 256, 256) { OutputMimeType = ContentTypes.Images.Webp },
            ct
        );

        return resized.IsDone ? resized.Result.Content : null;
    }
}
```

---

## Headless.Imaging

The pipeline behind `IImageResizer`, `IImageCompressor`, and `IImageInspector`, and the registration entry point.

### Setup

```bash
dotnet add package Headless.Imaging
```

```csharp
builder.Services.AddHeadlessImaging(imaging =>
    imaging
        .Configure(options => options.DefaultResizeMode = ImageResizeMode.Max)
        .UseNetVips() // from Headless.Imaging.NetVips
);
```

`Configure` takes an `IConfiguration` section, an `Action<ImagingOptions>`, or an `Action<ImagingOptions, IServiceProvider>`. Repeated calls apply in order.

**Verify:** the host fails to start with `OptionsValidationException` when `ImagingOptions` is invalid. Libvips loads only when a contributor is first resolved, so resolve `IImageResizer` once after `builder.Build()` to fail at startup instead of on the first upload. In a test, resize a small PNG and assert `result.IsDone`.

### Configuration

| `ImagingOptions` property | Default | Description |
| --- | --- | --- |
| `DefaultResizeMode` | `None` | Replaces `ImageResizeMode.Default` in a request. Must be a defined `ImageResizeMode` value. |

### Design and runtime behavior

- `IImageResizer`, `IImageCompressor`, and `IImageInspector` are singletons.
- Each chosen provider registers after the core services, in the order of the `Use…` calls.
- A non-seekable input is buffered into a `MemoryStream` for the length of one call, then the contributors read it.

### Write a contributor

Implement `IImageResizerContributor.TryResizeAsync`, `IImageCompressorContributor.TryCompressAsync`, or `IImageInspectorContributor.TryInspectAsync`, and register it as a singleton after `AddHeadlessImaging(...)` so it runs first. Return `Unsupported` (for example `ImageStreamResizeResult.NotSupported()`) for any input you do not handle, so the next contributor gets it. Return `Failed` only when no other contributor should try. An inspector contributor returns `Unsupported` for every format its resizer and compressor refuse, so inspection and processing agree.

---

## Headless.Imaging.NetVips

The libvips contributors for resize, compress, and inspect.

### Setup

```bash
dotnet add package Headless.Imaging.NetVips
dotnet add package NetVips.Native   # or install libvips 8.15 or later on the host
```

```csharp
builder.Services.AddHeadlessImaging(imaging =>
    imaging.UseNetVips(options =>
    {
        options.JpegQuality = 80;
        options.AvifQuality = 45;
        options.CropFocus = NetVipsCropFocus.Attention;
        options.MaxPixels = 50_000_000;
    })
);
```

`UseNetVips` takes no argument, an `IConfiguration` section, an `Action<NetVipsOptions>`, or an `Action<NetVipsOptions, IServiceProvider>`. Calling it more than once registers each contributor once.

**Verify:** the host fails to start with `OptionsValidationException` when `NetVipsOptions` is invalid. Resolving `IImageResizer` throws `InvalidOperationException` when libvips did not load or is older than 8.15. In a test, resize a small JPEG to `ImageResizeMode.Max, 100, 100` and assert `result.IsDone` and `result.Result.Width <= 100`.

### Choose a libvips source

The package references only the MIT-licensed `NetVips` wrapper, so the application picks the native library and its licence:

| Source | Use when | Cost |
| --- | --- | --- |
| `NetVips.Native` package | You want no host setup. This is the tested configuration. Ships for linux x64, arm64, and arm; linux-musl x64 and arm64; win x64 and arm64; osx x64 and arm64. | About 8 MB per platform. LGPL-3.0-or-later: an app you distribute (a public container image, an on-premises installer, a device) must offer the LGPL source and allow relinking. A service you run on your own servers carries no LGPL obligation. |
| System libvips 8.15 or later (`apt-get install libvips42t64` on Ubuntu 24.04 or Debian 13, `apk add vips`, `brew install vips`) | Your policy bans the GPL-3 family, or you want the operating system to patch libvips. | The codec set is whatever the distribution compiled in. Debian 12 ships libvips 8.14, which is too old. |

### Configuration

| `NetVipsOptions` property | Type | Default | Description |
| --- | --- | --- | --- |
| `JpegQuality` | `int` (1–100) | `75` | JPEG quality for resize output and compression. Huffman tables are always optimized. |
| `WebpQuality` | `int` (1–100) | `75` | Lossy WebP quality. |
| `AvifQuality` | `int` (1–100) | `50` | AVIF quality. |
| `PngCompressionLevel` | `int` (0–9) | `9` | zlib effort for PNG. PNG stays lossless at every level. |
| `JpegProgressive` | `bool` | `false` | Writes progressive JPEG, which a browser draws coarse to fine. Baseline otherwise. |
| `ChromaSubsampling` | `NetVipsChromaSubsampling` | `Auto` | Colour resolution for JPEG and AVIF. `Auto` subsamples below quality 90. `On` always subsamples (smallest files). `Off` never does (crisp coloured edges for text and logos). |
| `StripMetadata` | `bool` | `true` | Drops EXIF, XMP, IPTC, and other metadata and keeps the ICC profile. When on, the compressor first turns the pixels upright, because the orientation tag goes with the EXIF block. |
| `MaxPixels` | `long` (> 0) | `268402689` (16383²) | Largest width × height × frames of an input, checked from the header before decoding, and of a resize output. Over the limit, the call returns `Failed`. |
| `CropFocus` | `NetVipsCropFocus` | `Center` | The region `ImageResizeMode.Crop` keeps: `Center`, `Attention` (skin tones, saturated colour, and edges), or `Entropy` (most detail). Animations always crop at the center. |

### Design and runtime behavior

**Format allowlist.** Only the JPEG, PNG, WebP, GIF, TIFF, and AVIF loaders run. libvips detects the format from the magic bytes, and a HEIF file passes only when its header names AV1 (AVIF). Everything else is refused as `Unsupported` before a parser reads the bytes: SVG, PDF, ImageMagick formats, CSV, matrix, and the libvips `.v` format. A system libvips with more codecs does not widen the list. `NetVips.Native` has no BMP loader, no HEVC decoder for HEIC, and no JPEG XL.

**Strict decoding.** A truncated or corrupt file returns `Unsupported` with "The encoded image contains invalid content." instead of a partly grey image.

**Resize output.**

- The output is written as `OutputMimeType`, or else the source format. JPEG and WebP shrink while decoding, so a large photo is never decoded at full size.
- An animated GIF or WebP keeps every frame when the output is GIF or WebP. Each frame is cropped or padded on its own, and frame delays and the loop count carry over. Converting an animation to JPEG, PNG, TIFF, or AVIF keeps the first frame.
- Transparency written to JPEG, padding included, is flattened onto white.
- Resize output is 8-bit sRGB. Compression keeps the source bit depth.
- EXIF orientation is applied, so output is stored upright and the reported width and height are the displayed ones, for an `ImageResizeMode.None` pass-through too.

**Compression.** Re-encodes into the source format or `OutputMimeType` (JPEG, PNG, WebP, or AVIF) at the configured quality. Any allowed input converts, so an animated GIF compresses into an animated WebP. A GIF or TIFF without an `OutputMimeType`, or with an `OutputMimeType` of GIF or TIFF, returns `Unsupported`.

**Inspection reads only headers.** `Done` means the pipeline accepts the format, not that every pixel decodes. The inspector reports sizes above `MaxPixels` instead of refusing them.

**Header-first reads.** Every call reads from the start of the stream, wherever its position was left. The format check, the `MaxPixels` check, and an `ImageResizeMode.None` pass-through are decided from the header, so refusing an oversized camera JPEG reads at most a few hundred kilobytes. A GIF or WebP is read whole to count frames. A HEIC, or a corrupt or truncated file of an allowed format, is read whole before it is refused.

**Stream types.** libvips reads a `MemoryStream` or a `FileStream` directly, with no copy. Any other stream, including `IFormFile.OpenReadStream()`, is copied once into an exact-size buffer first, because ASP.NET Core throws on a synchronous read from a request body that is not fully buffered. To avoid the copy for large uploads, copy the upload to a temporary `FileStream` or pass a `MemoryStream`.

**Threads and cancellation.** libvips decodes and encodes on the calling thread and its own worker threads, one per core by default. Cancelling the token stops the workers within a tile or two and throws `OperationCanceledException`.

**Process-wide libvips settings.** libvips keeps a process-wide operation cache (100 operations, 100 MB, 100 open files by default) and a per-operation thread pool. The package leaves both to the application. The cache never keeps your input stream alive, but it can hold operation results. A host that resizes many distinct images can turn it off at startup. Write the `global::` prefix to avoid a clash with the `Headless.Imaging` namespace:

```csharp
global::NetVips.Cache.Max = 0;               // no operation cache
global::NetVips.NetVips.Concurrency = 2;     // threads per libvips operation
```

**Logs.** Refused formats, oversized images, and invalid content log at `Information`.
