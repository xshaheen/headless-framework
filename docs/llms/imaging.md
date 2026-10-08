---
domain: Imaging
packages: Imaging.Abstractions, Imaging, Imaging.NetVips
---

# Imaging

> Image resizing and compression pipeline with contributor-based extensibility, powered by libvips through NetVips.

## Orientation

Install all three packages for a complete imaging pipeline:

- `Headless.Imaging.Abstractions` — interfaces (`IImageResizer`, `IImageCompressor`) and argument/result types
- `Headless.Imaging` — orchestration layer, `ImagingOptions`, DI registration via `AddHeadlessImaging(imaging => …)`
- `Headless.Imaging.NetVips` — libvips-backed contributors chosen with `imaging.UseNetVips()`; the application also adds `NetVips.Native` or installs libvips

Typical registration:

```csharp
builder.Services.AddHeadlessImaging(imaging =>
    imaging
        .Configure(options => options.DefaultResizeMode = ImageResizeMode.Max)
        .UseNetVips(options =>
        {
            options.JpegQuality = 80;
            options.WebpQuality = 80;
        })
);
```

Resizes JPEG, PNG, WebP, GIF (animated included), TIFF, and AVIF; compresses JPEG, PNG, WebP, and AVIF. BMP is not supported.

## Agent Rules

- Always install all three packages (Abstractions + Core + NetVips) for a working pipeline, plus a libvips: `NetVips.Native` for the bundled binaries, or libvips 8.15 or later on the host. Abstractions alone provides no implementation; Core alone has no image-processing backend; `Headless.Imaging.NetVips` alone has no native library, and its contributors throw `InvalidOperationException` when first resolved.
- Inject `IImageResizer` for resizing, `IImageCompressor` for compression. Both are registered as singletons.
- `ImageResizeArgs` is constructed via its constructors — it has no settable `Width`/`Height`/`Mode` object-initializer properties. Pick the right overload: `(ImageResizeMode, int width, int? height, string? mimeType)`, `(ImageResizeMode, int? width, int height, string? mimeType)`, or `(ImageResizeMode, int width, int height, string? mimeType)`. Negative values throw `ArgumentException` at construction time.
- Check `result.IsDone` before accessing `result.Result`. When `IsDone` is `false`, `result.Error` is non-null and `result.Result` is null. This is enforced by `[MemberNotNullWhen]` attributes.
- For `ImageStreamResizeResult`: access the processed image via `result.Result.Content` (a `Stream`), not `.Stream`. The result also carries `result.Result.MimeType`, `result.Result.Width`, and `result.Result.Height`.
- `ImageCompressArgs` accepts only an optional `mimeType`. It carries no quality setting — quality is governed by `NetVipsOptions` (`JpegQuality`, `WebpQuality`, `AvifQuality`, `PngCompressionLevel`).
- The `mimeType` on `ImageResizeArgs` and `ImageCompressArgs` is a hint about the input, not an output format: a contributor skips a hinted type it does not handle, and otherwise detects the format from the bytes. The output keeps the source format, and `result.Result.MimeType` is the detected one.
- `ImageResizeMode.Default` is the zero/unset value (`default(ImageResizeMode)`) and acts as the sentinel meaning "use the pipeline default". It is resolved at runtime to `ImagingOptions.DefaultResizeMode`. If `DefaultResizeMode` is also `None` (the default), the image is returned unchanged.
- `ImageResizeMode.None` is an explicit, distinct value that skips resizing entirely and returns the original stream. Use `Max`, `Crop`, `Pad`, `BoxPad`, `Min`, or `Stretch` for actual resizing.
- The compressor returns `ImageProcessState.Failed` when the compressed output is larger than the original — it never bloats a file. Check `result.IsDone` to detect this case.
- Contributors are iterated in **reverse registration order**. The last-registered contributor is tried first. A custom contributor registered after `AddHeadlessImaging(...)` takes priority over the NetVips contributors.
- Non-seekable input streams are automatically buffered into a `MemoryStream` by the resize and compress pipelines (the internal `IImageResizer`/`IImageCompressor` implementations). Callers do not need to buffer first.
- Returned `Stream` objects in results are owned by the caller. Dispose them when done.
- GIF and TIFF are supported for resize but not for compression. Passing a GIF or TIFF stream to `IImageCompressor` returns `ImageProcessState.Unsupported`. BMP, SVG, PDF, HEIC, and JPEG XL are not supported by either contributor and return `Unsupported`.
- An image above `NetVipsOptions.MaxPixels`, or a resize request whose output would exceed it, returns `ImageProcessState.Failed`, not `Unsupported`, so no other contributor is offered the image.
- Register imaging once, with `services.AddHeadlessImaging(imaging => imaging.UseNetVips())`. `AddHeadlessImaging` throws `InvalidOperationException` when no provider is chosen or when it is called a second time on the same service collection. The NetVips contributor implementations are internal — register them only through `UseNetVips()`, never by type.

---

## Core Concepts

### Abstraction layer

`IImageResizer` and `IImageCompressor` are the only types application code should reference. Both accept a `Stream` and typed args, and return a result carrying either a processed `Stream` or an error.

### Contributor pipeline

`Headless.Imaging` does not process images itself. It iterates a registered list of `IImageResizerContributor` / `IImageCompressorContributor` instances in reverse order and returns the first non-`Unsupported` result. A contributor returns `ImageProcessState.Unsupported` to signal that it does not handle the given format; the orchestrator then tries the next contributor. This design allows multiple processing backends to coexist.

### Result model

All results derive from `ImageProcessResult<T>` with three states:

| `ImageProcessState` | Meaning |
|---|---|
| `Done` | Processing succeeded. `Result` is non-null; `Error` is null. |
| `Unsupported` | No contributor could handle the format. `Error` describes why. |
| `Failed` | A contributor attempted processing but it failed (e.g., compressed output was larger). |

`IsDone` is a `[MemberNotNullWhen]`-annotated shorthand: `true` iff `State == Done`.

### Resize modes

`ImageResizeMode` values. When only one side is given, the other follows the source aspect ratio before the mode applies:

| Mode | Behavior |
|---|---|
| `Default` | Zero/unset sentinel. Resolved at runtime to `ImagingOptions.DefaultResizeMode`. |
| `None` | No resize — original stream returned as-is. |
| `Max` | Scale down to fit within width × height, preserving aspect ratio. Never upscales. |
| `Crop` | Fill and crop to exact width × height. `NetVipsOptions.CropFocus` picks the kept region. |
| `Pad` | Resize to fit, then pad to exact width × height. The padding is transparent when the format has alpha, black for JPEG. |
| `BoxPad` | Pad to width × height without resizing a source that fits; a larger source behaves like `Pad`. |
| `Min` | Put the side whose target is nearest its source length exactly on the target, and scale the other side with the aspect ratio; no crop. Never upscales: a source smaller than the box on either side keeps its size. |
| `Stretch` | Stretch to exact width × height, ignoring aspect ratio. |

---

## Headless.Imaging.Abstractions

Defines the provider-agnostic contracts for image processing operations.

### API and behavior

- `IImageResizer` — resize interface: `ResizeAsync(Stream, ImageResizeArgs, CancellationToken)`
- `IImageCompressor` — compression interface: `CompressAsync(Stream, ImageCompressArgs, CancellationToken)`
- `ImageResizeArgs` — resize parameters: mode, width, height, optional MIME type override
- `ImageCompressArgs` — compression parameters: optional MIME type override
- `ImageResizeMode` — enum of resize strategies (`Default` (zero/unset sentinel), `None`, `Max`, `Crop`, `Pad`, `BoxPad`, `Min`, `Stretch`)
- `ImageStreamResizeResult` / `ImageStreamCompressResult` — typed result wrappers
- `ImageProcessResult<T>` — base result with `IsDone`, `State`, `Result`, `Error`
- `ImageProcessState` — `Done`, `Unsupported`, `Failed`
- `ImageResizeContent<TContent>` — carries `Content`, `MimeType`, `Width`, `Height` for resize results

### Install

```bash
dotnet add package Headless.Imaging.Abstractions
```

### Setup and use

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

### Runtime behavior

None.

---

## Headless.Imaging

Orchestration layer that routes image processing calls to registered contributors.

### API and behavior

- `IImageResizer` — resize entry point resolved from DI; the internal default implementation iterates `IImageResizerContributor` registrations (in reverse order) until one succeeds
- `IImageCompressor` — compress entry point resolved from DI; the internal default implementation iterates `IImageCompressorContributor` registrations (in reverse order) until one succeeds
- `IImageResizerContributor` — contributor interface: `TryResizeAsync(Stream, ImageResizeArgs, CancellationToken)`
- `IImageCompressorContributor` — contributor interface: `TryCompressAsync(Stream, ImageCompressArgs, CancellationToken)`
- `ImagingOptions` — `DefaultResizeMode` applied when args carry `ImageResizeMode.Default`
- `AddHeadlessImaging(Action<HeadlessImagingSetupBuilder>)` — registers the pipeline and the providers chosen on the builder
- `HeadlessImagingSetupBuilder` — `Configure(...)` binds `ImagingOptions` (`IConfiguration`, `Action<ImagingOptions>`, or `Action<ImagingOptions, IServiceProvider>`); provider packages add `Use…` members
- `IImagingProviderOptionsExtension` — the hook a provider's `Use…` member registers through `RegisterExtension`; `AddHeadlessImaging` calls its `AddServices` after the core services
- Automatic MemoryStream buffering for non-seekable input streams
- Options validation via FluentValidation at startup

### Design constraints

Contributors are enumerated in reverse order of DI registration. This mirrors the last-in-wins overriding model: a contributor registered after `AddHeadlessImaging(...)` takes precedence over the NetVips contributors without requiring any removal. A contributor signals non-support by returning `ImageProcessState.Unsupported`; the orchestrator seeks the stream back to the start and tries the next contributor.

### Install

```bash
dotnet add package Headless.Imaging
```

### Setup and use

```csharp
builder.Services.AddHeadlessImaging(imaging =>
    imaging
        .Configure(options => options.DefaultResizeMode = ImageResizeMode.Max)
        .UseNetVips() // from Headless.Imaging.NetVips
);
```

`Configure` has three overloads, and repeated calls apply in order:

```csharp
// Bind from IConfiguration section
imaging.Configure(config.GetSection("Headless:Imaging"));

// Configure with action
imaging.Configure(options => options.DefaultResizeMode = ImageResizeMode.Crop);

// Configure with action + IServiceProvider
imaging.Configure((options, sp) => options.DefaultResizeMode = ImageResizeMode.Max);
```

### Configuration

`ImagingOptions` (one property):

| Property | Type | Default | Description |
|---|---|---|---|
| `DefaultResizeMode` | `ImageResizeMode` | `None` | Applied when `ImageResizeArgs.Mode` is `Default`. `None` means no resize fallback. |

### Runtime behavior

- Registers `IImageResizer` as singleton (internal default implementation)
- Registers `IImageCompressor` as singleton (internal default implementation)
- Validates `ImagingOptions` with FluentValidation when the options are first resolved
- Runs each chosen provider's registration after the core services, in the order of the `Use…` calls

---


## Headless.Imaging.NetVips

libvips-backed contributors for image resizing and compression, through the NetVips binding.

### API and behavior

- Internal libvips `IImageResizerContributor` (registered by `UseNetVips`): resizes JPEG, PNG, WebP, GIF, TIFF, and AVIF into the source format. It runs libvips `thumbnail`, which shrinks JPEG and WebP while decoding, so a large photo never decodes at full size.
- Internal libvips `IImageCompressorContributor` (registered by `UseNetVips`): re-encodes JPEG, PNG, WebP, and AVIF in the source format at the configured quality; returns `Failed` unless the output is strictly smaller than the input.
- `NetVipsOptions`: per-format quality, PNG compression level, metadata stripping, the pixel limit, and the crop focus.
- `NetVipsCropFocus`: `Center`, `Attention` (skin tones, saturated colour, and edges), or `Entropy` (most detail).
- Animated GIF and WebP keep every frame: each frame is cropped or padded on its own, and the frame delays and loop count carry over.
- Resize output is 8-bit sRGB: libvips `thumbnail` converts 16-bit and other colour spaces. Compression keeps the source depth.
- The EXIF orientation is applied, so resize output is stored upright and the reported width and height are the displayed ones. `ImageResizeMode.None` returns the caller's stream with its stored width and height.

### Design constraints

**The package references only the MIT-licensed `NetVips` wrapper, not the native binaries.** `NetVips.Native` bundles libvips and its codecs under LGPL-3.0-or-later. A web app that runs on its own servers carries no LGPL obligation, but an app that distributes its binaries, for example in a public container image, an on-premises installer, or a device, must offer the LGPL source and allow relinking, and some company policies ban the GPL-3 family outright. Bundling the natives would make that decision for every consumer, so the application makes it:

- **`NetVips.Native`** (`dotnet add package NetVips.Native`): zero setup, about 8 MB per platform; linux x64/arm64/arm, linux-musl x64/arm64, win x64/arm64, osx x64/arm64. This is the tested configuration.
- **A system libvips 8.15 or later** (`apt-get install libvips42t64` on Ubuntu 24.04 or Debian 13, `apk add vips`, `brew install vips`): the operating system's security updates patch it, and its licence is the distribution's build (libvips itself is LGPL-2.1-or-later). Its codec set is whatever the distribution compiled in. Debian 12 ships libvips 8.14, which is too old.

Without either, the contributors throw `InvalidOperationException` naming both options when the container first resolves them, which is the first `IImageResizer` or `IImageCompressor` resolution.

**Untrusted input is fenced by an allowlist, not by libvips' global switch.** libvips sniffs the format from the magic bytes, and only the JPEG, PNG, WebP, GIF, TIFF, and HEIF loaders may run; HEIF passes only when its header names AV1 (AVIF). Everything else is refused before its parser sees the bytes: SVG (librsvg), PDF, ImageMagick formats, CSV, matrix, and the native `.v` format. This per-call check replaces `NetVips.BlockUntrusted`, which is process-wide and blocks only some of those loaders.

**Formats depend on the libvips build.** The `NetVips.Native` 8.18 bundle has no BMP loader (libvips reads BMP only through ImageMagick, which the bundle omits and the allowlist refuses), no HEVC encoder (so HEIC is refused), and no JPEG XL. A system libvips with more codecs does not widen the allowlist.

**Decoding is strict.** Loaders run with `fail_on=error`, so a truncated or corrupt file returns `Unsupported` with "The encoded image contains invalid content." instead of a half-grey image.

**Process-wide libvips settings stay with the application.** libvips keeps an operation cache (100 operations, 100 MB, 100 open files by default) and a worker pool per operation (one thread per core). Both are process globals, so the package does not own them. The cache does not hold the input buffers; a host that resizes many distinct images can still turn it off at startup, where the `global::` prefix avoids a clash with the `Headless.Imaging` namespace:

```csharp
global::NetVips.Cache.Max = 0;               // no operation cache
global::NetVips.NetVips.Concurrency = 2;     // threads per libvips operation
```

**Libvips work is synchronous.** The contributors read the input stream asynchronously, then decode and encode on the calling thread. The cancellation token is checked before the work starts; a running libvips operation is not interrupted.

### Install

```bash
dotnet add package Headless.Imaging.NetVips
dotnet add package NetVips.Native   # or install libvips 8.15+ on the host
```

### Setup and use

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

`UseNetVips` has four overloads:

```csharp
// Default options
imaging.UseNetVips();

// Bind from IConfiguration section
imaging.UseNetVips(config.GetSection("Headless:Imaging:NetVips"));

// Configure with action
imaging.UseNetVips(options => options.WebpQuality = 85);

// Configure with action + IServiceProvider
imaging.UseNetVips((options, sp) => options.WebpQuality = 85);
```

### Configuration

`NetVipsOptions`:

| Property | Type | Default | Description |
|---|---|---|---|
| `JpegQuality` | `int` (1–100) | `75` | JPEG quality for resize output and compression. Huffman tables are always optimized. |
| `WebpQuality` | `int` (1–100) | `75` | Lossy WebP quality. |
| `AvifQuality` | `int` (1–100) | `50` | AVIF (AV1) quality; 50 is libvips' own default. |
| `PngCompressionLevel` | `int` (0–9) | `9` | zlib effort for PNG output; PNG stays lossless at every level. |
| `StripMetadata` | `bool` | `true` | Drops EXIF, XMP, IPTC, and other metadata, keeping only the ICC profile. When on, the compressor first turns the pixels upright, because the orientation tag goes with the EXIF block. |
| `MaxPixels` | `long` (> 0) | `268402689` (16383²) | Largest width × height × frames of an input, checked from the header before decoding, and of a resize output. Exceeding it returns `Failed`. |
| `CropFocus` | `NetVipsCropFocus` | `Center` | The region `ImageResizeMode.Crop` keeps. Animations always crop at the center, because a content-aware window would move from frame to frame. |

Validation (applied at startup): qualities between 1 and 100, `PngCompressionLevel` between 0 and 9, `MaxPixels` positive, `CropFocus` a defined value.

### Runtime behavior

- Registers `IImageResizerContributor` as singleton (internal NetVips resize contributor)
- Registers `IImageCompressorContributor` as singleton (internal NetVips compress contributor)
- Registers each contributor once, however many times `UseNetVips` is called
- Checks on first resolution that libvips 8.15 or later loaded
- Logs refused formats, oversized images, and invalid content at `Information`
