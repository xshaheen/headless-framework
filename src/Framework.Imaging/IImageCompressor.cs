using Framework.Arguments;
using Framework.Imaging.Contracts;

namespace Framework.Imaging;

public interface IImageCompressor
{
    Task<ImageCompressResult<Stream>> CompressAsync(
        Stream stream,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    );

    Task<ImageCompressResult<byte[]>> CompressAsync(
        byte[] bytes,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class ImageCompressor : IImageCompressor
{
    private IEnumerable<IImageCompressorContributor> ImageCompressorContributors { get; }

    public ImageCompressor(IEnumerable<IImageCompressorContributor> imageCompressorContributors)
    {
        ImageCompressorContributors = imageCompressorContributors.Reverse();
    }

    public async Task<ImageCompressResult<Stream>> CompressAsync(
        Stream stream,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(stream);

        if (!stream.CanRead)
        {
            return new(stream, ImageProcessState.Unsupported);
        }

        if (!stream.CanSeek)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            _SeekToBegin(memoryStream);
            stream = memoryStream;
        }

        foreach (var imageCompressorContributor in ImageCompressorContributors)
        {
            var result = await imageCompressorContributor.TryCompressAsync(stream, mimeType, cancellationToken);

            _SeekToBegin(stream);

            if (result.State == ImageProcessState.Unsupported)
            {
                continue;
            }

            return result;
        }

        return new ImageCompressResult<Stream>(stream, ImageProcessState.Unsupported);
    }

    public async Task<ImageCompressResult<byte[]>> CompressAsync(
        byte[] bytes,
        string? mimeType = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(bytes);

        foreach (var imageCompressorContributor in ImageCompressorContributors)
        {
            var result = await imageCompressorContributor.TryCompressAsync(bytes, mimeType, cancellationToken);

            if (result.State == ImageProcessState.Unsupported)
            {
                continue;
            }

            return result;
        }

        return new ImageCompressResult<byte[]>(bytes, ImageProcessState.Unsupported);
    }

    private static void _SeekToBegin(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }
    }
}
