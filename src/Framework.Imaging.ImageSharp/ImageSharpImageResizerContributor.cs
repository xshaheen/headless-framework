// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Constants;
using Framework.Imaging.ImageSharp.Internals;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Framework.Imaging.ImageSharp;

public sealed class ImageSharpImageResizerContributor(ILogger<ImageSharpImageResizerContributor> logger)
    : IImageResizerContributor
{
    public async Task<ImageStreamResizeResult> TryResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(args.MimeType) && !_CanResize(args.MimeType))
        {
            return ImageStreamResizeResult.NotSupportedMimeType(args.MimeType);
        }

        var (image, error) = await LoadImageHelpers.TryLoad(stream, logger, cancellationToken);

        if (error is not null)
        {
            return ImageStreamResizeResult.NotSupported(error);
        }

        Debug.Assert(image is not null);
        var format = image.Metadata.DecodedImageFormat;

        if (format is null)
        {
            return ImageStreamResizeResult.NotSupported();
        }

        var mimeType = args.MimeType ?? format.DefaultMimeType;

        if (!_CanResize(mimeType))
        {
            return ImageStreamResizeResult.NotSupportedMimeType(mimeType);
        }

        var resizeMode = _GetResizeMode(args.Mode);

        if (resizeMode is null)
        {
            return ImageStreamResizeResult.Done(stream, mimeType, image.Width, image.Height);
        }

        image.Mutate(x => x.Resize(new ResizeOptions { Size = _GetSize(args), Mode = resizeMode.Value }));

        var memoryStream = new MemoryStream();

        try
        {
            await image.SaveAsync(memoryStream, format, cancellationToken);

            memoryStream.Position = 0;

            return ImageStreamResizeResult.Done(memoryStream, mimeType, image.Width, image.Height);
        }
        catch
        {
            await memoryStream.DisposeAsync();

            throw;
        }
    }

    private static ResizeMode? _GetResizeMode(ImageResizeMode mode)
    {
        return mode switch
        {
            ImageResizeMode.None or ImageResizeMode.Default => null,
            ImageResizeMode.Stretch => ResizeMode.Stretch,
            ImageResizeMode.BoxPad => ResizeMode.BoxPad,
            ImageResizeMode.Min => ResizeMode.Min,
            ImageResizeMode.Max => ResizeMode.Max,
            ImageResizeMode.Crop => ResizeMode.Crop,
            ImageResizeMode.Pad => ResizeMode.Pad,
            _ => throw new InvalidOperationException($"Unknown {nameof(ImageResizeMode)}={mode}"),
        };
    }

    private static bool _CanResize(string? mimeType)
    {
        return mimeType switch
        {
            ContentTypes.Images.Jpeg => true,
            ContentTypes.Images.Png => true,
            ContentTypes.Images.Gif => true,
            ContentTypes.Images.Bmp => true,
            ContentTypes.Images.Tiff => true,
            ContentTypes.Images.Webp => true,
            _ => false,
        };
    }

    private static Size _GetSize(ImageResizeArgs resizeArgs)
    {
        var size = new Size();

        if (resizeArgs.Width > 0)
        {
            size.Width = resizeArgs.Width;
        }

        if (resizeArgs.Height > 0)
        {
            size.Height = resizeArgs.Height;
        }

        return size;
    }
}
