using Framework.Imaging.Contracts;

namespace Framework.Imaging;

public interface IImageResizerContributor
{
    Task<ImageResizeResult<Stream>> TryResizeAsync(
        Stream stream,
        ImageResizeArgs resizeArgs,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    );

    Task<ImageResizeResult<byte[]>> TryResizeAsync(
        byte[] bytes,
        ImageResizeArgs resizeArgs,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    );
}
