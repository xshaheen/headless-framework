using Framework.Imaging.Contracts;
using Framework.Kernel.Checks;
using Microsoft.Extensions.Options;

namespace Framework.Imaging;

public interface IImageResizer
{
    Task<ImageStreamResizeResult> ResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}

public sealed class ImageResizer : IImageResizer
{
    private readonly IEnumerable<IImageResizerContributor> _resizerContributors;
    private readonly ImagingOptions _imagingOptions;

    public ImageResizer(
        IEnumerable<IImageResizerContributor> imageResizerContributors,
        IOptions<ImagingOptions> imageResizeOptions
    )
    {
        _resizerContributors = imageResizerContributors.Reverse();
        _imagingOptions = imageResizeOptions.Value;
    }

    public async Task<ImageStreamResizeResult> ResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(stream);

        _ChangeDefaultResizeMode(args);

        if (!stream.CanRead)
        {
            return ImageStreamResizeResult.CannotRead();
        }

        if (!stream.CanSeek)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            _SeekToBegin(memoryStream);
            stream = memoryStream;
        }

        foreach (var resizerContributor in _resizerContributors)
        {
            var result = await resizerContributor.TryResizeAsync(stream, args, cancellationToken);

            _SeekToBegin(stream);

            if (result.State is ImageProcessState.Unsupported)
            {
                continue;
            }

            return result;
        }

        return ImageStreamResizeResult.NotSupported();
    }

    private void _ChangeDefaultResizeMode(ImageResizeArgs resizeArgs)
    {
        if (resizeArgs.Mode == ImageResizeMode.Default)
        {
            resizeArgs.Mode = _imagingOptions.DefaultResizeMode;
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
