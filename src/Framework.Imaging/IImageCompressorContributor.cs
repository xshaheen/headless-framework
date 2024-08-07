using Framework.Imaging.Contracts;

namespace Framework.Imaging;

public interface IImageCompressorContributor
{
    Task<ImageCompressResult<Stream>> TryCompressAsync(
        Stream stream,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    );

    Task<ImageCompressResult<byte[]>> TryCompressAsync(
        byte[] bytes,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    );
}
