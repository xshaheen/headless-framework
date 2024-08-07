using Framework.Arguments;
using Framework.Imaging.Contracts;
using Microsoft.Extensions.Options;

namespace Framework.Imaging;

public interface IImageResizer
{
    Task<ImageResizeResult<Stream>> ResizeAsync(
        Stream stream,
        ImageResizeArgs resizeArgs,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    );

    Task<ImageResizeResult<byte[]>> ResizeAsync(
        byte[] bytes,
        ImageResizeArgs resizeArgs,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class ImageResizer : IImageResizer
{
    private readonly IEnumerable<IImageResizerContributor> _imageResizerContributors;
    private readonly ImageResizeOptions _imageResizeOptions;

    public ImageResizer(
        IEnumerable<IImageResizerContributor> imageResizerContributors,
        IOptions<ImageResizeOptions> imageResizeOptions
    )
    {
        _imageResizerContributors = imageResizerContributors.Reverse();
        _imageResizeOptions = imageResizeOptions.Value;
    }

    public async Task<ImageResizeResult<Stream>> ResizeAsync(
        Stream stream,
        ImageResizeArgs resizeArgs,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(stream);

        _ChangeDefaultResizeMode(resizeArgs);

        if (!stream.CanRead)
        {
            return new ImageResizeResult<Stream>(stream, ImageProcessState.Unsupported);
        }

        if (!stream.CanSeek)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            _SeekToBegin(memoryStream);
            stream = memoryStream;
        }

        foreach (var imageResizerContributor in _imageResizerContributors)
        {
            var result = await imageResizerContributor.TryResizeAsync(stream, resizeArgs, mimeType, cancellationToken);

            _SeekToBegin(stream);

            if (result.State == ImageProcessState.Unsupported)
            {
                continue;
            }

            return result;
        }

        return new ImageResizeResult<Stream>(stream, ImageProcessState.Unsupported);
    }

    public async Task<ImageResizeResult<byte[]>> ResizeAsync(
        byte[] bytes,
        ImageResizeArgs resizeArgs,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(bytes);

        _ChangeDefaultResizeMode(resizeArgs);

        foreach (var imageResizerContributor in _imageResizerContributors)
        {
            var result = await imageResizerContributor.TryResizeAsync(bytes, resizeArgs, mimeType, cancellationToken);

            if (result.State == ImageProcessState.Unsupported)
            {
                continue;
            }

            return result;
        }

        return new ImageResizeResult<byte[]>(bytes, ImageProcessState.Unsupported);
    }

    private void _ChangeDefaultResizeMode(ImageResizeArgs resizeArgs)
    {
        if (resizeArgs.Mode == ImageResizeMode.Default)
        {
            resizeArgs.Mode = _imageResizeOptions.DefaultResizeMode;
        }
    }

    private static void _SeekToBegin(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }
    }
}
