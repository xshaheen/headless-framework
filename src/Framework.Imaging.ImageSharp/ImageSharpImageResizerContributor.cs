using Framework.BuildingBlocks.Constants;
using Framework.Imaging.Contracts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Framework.Imaging.ImageSharp;

public sealed class ImageSharpImageResizerContributor : IImageResizerContributor
{
    private readonly Dictionary<ImageResizeMode, ResizeMode> _resizeModeMap =
        new()
        {
            { ImageResizeMode.None, default },
            { ImageResizeMode.Stretch, ResizeMode.Stretch },
            { ImageResizeMode.BoxPad, ResizeMode.BoxPad },
            { ImageResizeMode.Min, ResizeMode.Min },
            { ImageResizeMode.Max, ResizeMode.Max },
            { ImageResizeMode.Crop, ResizeMode.Crop },
            { ImageResizeMode.Pad, ResizeMode.Pad }
        };

    public async Task<ImageResizeResult<Stream>> TryResizeAsync(
        Stream stream,
        ImageResizeArgs resizeArgs,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(mimeType) && !_CanResize(mimeType))
        {
            return new(stream, ImageProcessState.Unsupported);
        }

        var image = await Image.LoadAsync(stream, cancellationToken);

        if (!_CanResize(image.Metadata.DecodedImageFormat!.DefaultMimeType))
        {
            return new(stream, ImageProcessState.Unsupported);
        }

        if (_resizeModeMap.TryGetValue(resizeArgs.Mode, out var resizeMode))
        {
            image.Mutate(x => x.Resize(new ResizeOptions { Size = _GetSize(resizeArgs), Mode = resizeMode }));
        }
        else
        {
            throw new NotSupportedException("Resize mode " + resizeArgs.Mode + "is not supported!");
        }

        var memoryStream = new MemoryStream();

        try
        {
            await image.SaveAsync(
                memoryStream,
                image.Metadata.DecodedImageFormat,
                cancellationToken: cancellationToken
            );

            memoryStream.Position = 0;

            return new(memoryStream, ImageProcessState.Done);
        }
        catch
        {
            await memoryStream.DisposeAsync();

            throw;
        }
    }

    public async Task<ImageResizeResult<byte[]>> TryResizeAsync(
        byte[] bytes,
        ImageResizeArgs resizeArgs,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(mimeType) && !_CanResize(mimeType))
        {
            return new(bytes, ImageProcessState.Unsupported);
        }

        using var ms = new MemoryStream(bytes);

        var result = await TryResizeAsync(ms, resizeArgs, mimeType, cancellationToken);

        if (result.State is not ImageProcessState.Done)
        {
            return new(bytes, result.State);
        }

        var newBytes = await result.Result.GetAllBytesAsync(cancellationToken);

        await result.Result.DisposeAsync();

        return new(newBytes, result.State);
    }

    private static bool _CanResize(string? mimeType)
    {
        return mimeType switch
        {
            ContentTypes.Image.Jpeg => true,
            ContentTypes.Image.Png => true,
            ContentTypes.Image.Gif => true,
            ContentTypes.Image.Bmp => true,
            ContentTypes.Image.Tiff => true,
            ContentTypes.Image.Webp => true,
            _ => false
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
