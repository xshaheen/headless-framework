// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Imaging.Contracts;

namespace Framework.Imaging;

public interface IImageCompressor
{
    Task<ImageStreamCompressResult> CompressAsync(
        Stream stream,
        ImageCompressArgs args,
        CancellationToken cancellationToken = default
    );
}

public sealed class ImageCompressor(IEnumerable<IImageCompressorContributor> contributors) : IImageCompressor
{
    private readonly IEnumerable<IImageCompressorContributor> _compressorContributors = contributors.Reverse();

    public async Task<ImageStreamCompressResult> CompressAsync(
        Stream stream,
        ImageCompressArgs args,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(stream);

        if (!stream.CanRead)
        {
            return ImageStreamCompressResult.CannotRead();
        }

        if (!stream.CanSeek)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            _SeekToBegin(memoryStream);
            stream = memoryStream;
        }

        foreach (var imageCompressorContributor in _compressorContributors)
        {
            var result = await imageCompressorContributor.TryCompressAsync(stream, args, cancellationToken);

            _SeekToBegin(stream);

            if (result.State == ImageProcessState.Unsupported)
            {
                continue;
            }

            return result;
        }

        return ImageStreamCompressResult.NotSupported();
    }

    private static void _SeekToBegin(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }
    }
}
