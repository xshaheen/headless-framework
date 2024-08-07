using Framework.BuildingBlocks.Constants;
using Framework.Imaging.Contracts;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace Framework.Imaging.ImageSharp;

public sealed class ImageSharpImageCompressorContributor : IImageCompressorContributor
{
    private readonly ImageSharpCompressOptions _options;

    public ImageSharpImageCompressorContributor(IOptions<ImageSharpCompressOptions> options)
    {
        _options = options.Value;
    }

    public async Task<ImageCompressResult<Stream>> TryCompressAsync(
        Stream stream,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(mimeType) && !_CanCompress(mimeType))
        {
            return new(stream, ImageProcessState.Unsupported);
        }

        var image = await Image.LoadAsync(stream, cancellationToken);

        if (!_CanCompress(image.Metadata.DecodedImageFormat!.DefaultMimeType))
        {
            return new(stream, ImageProcessState.Unsupported);
        }

        var memoryStream = await _GetStreamFromImageAsync(image, image.Metadata.DecodedImageFormat, cancellationToken);

        if (memoryStream.Length < stream.Length)
        {
            return new(memoryStream, ImageProcessState.Done);
        }

        await memoryStream.DisposeAsync();

        return new(stream, ImageProcessState.Canceled);
    }

    public async Task<ImageCompressResult<byte[]>> TryCompressAsync(
        byte[] bytes,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(mimeType) && !_CanCompress(mimeType))
        {
            return new(bytes, ImageProcessState.Unsupported);
        }

        using var ms = new MemoryStream(bytes);
        var result = await TryCompressAsync(ms, mimeType, cancellationToken);

        if (result.State is not ImageProcessState.Done)
        {
            return new(bytes, result.State);
        }

        var newBytes = await result.Result.GetAllBytesAsync(cancellationToken);
        await result.Result.DisposeAsync();

        return new(newBytes, result.State);
    }

    private async Task<Stream> _GetStreamFromImageAsync(
        Image image,
        IImageFormat format,
        CancellationToken cancellationToken = default
    )
    {
        var memoryStream = new MemoryStream();

        try
        {
            await image.SaveAsync(memoryStream, _GetEncoder(format), cancellationToken: cancellationToken);

            memoryStream.Position = 0;

            return memoryStream;
        }
        catch
        {
            await memoryStream.DisposeAsync();

            throw;
        }
    }

    private IImageEncoder _GetEncoder(IImageFormat format)
    {
        return format.DefaultMimeType switch
        {
            ContentTypes.Image.Jpeg => _options.JpegEncoder,
            ContentTypes.Image.Png => _options.PngEncoder,
            ContentTypes.Image.Webp => _options.WebpEncoder,
            _ => throw new NotSupportedException($"No encoder available for the given format: {format.Name}"),
        };
    }

    private static bool _CanCompress(string? mimeType)
    {
        return mimeType switch
        {
            ContentTypes.Image.Jpeg => true,
            ContentTypes.Image.Png => true,
            ContentTypes.Image.Webp => true,
            _ => false
        };
    }
}
