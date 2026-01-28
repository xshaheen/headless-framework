// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Imaging;

namespace Headless.Imaging;

public sealed class ImageCompressor(IEnumerable<IImageCompressorContributor> contributors) : IImageCompressor
{
    private readonly IEnumerable<IImageCompressorContributor> _contributors = contributors.Reverse();

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

        foreach (var compressorContributor in _contributors)
        {
            var result = await compressorContributor.TryCompressAsync(stream, args, cancellationToken);

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
