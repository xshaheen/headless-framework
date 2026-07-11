// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Imaging;

/// <summary>
/// Default <see cref="IImageCompressor"/> implementation that delegates to a chain of
/// <see cref="IImageCompressorContributor"/> instances resolved from DI.
/// </summary>
/// <remarks>
/// Contributors are iterated in reverse registration order (last-registered wins). Each
/// contributor is given the same seekable stream; the stream is rewound to the beginning
/// after every attempt. If all contributors return <see cref="ImageProcessState.Unsupported"/>,
/// the result is <c>ImageStreamCompressResult.NotSupported()</c>.
/// </remarks>
internal sealed class ImageCompressor(IEnumerable<IImageCompressorContributor> contributors) : IImageCompressor
{
    private readonly IEnumerable<IImageCompressorContributor> _contributors = contributors.Reverse();

    /// <inheritdoc />
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
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            _SeekToBegin(memoryStream);
            stream = memoryStream;
        }

        foreach (var compressorContributor in _contributors)
        {
            var result = await compressorContributor
                .TryCompressAsync(stream, args, cancellationToken)
                .ConfigureAwait(false);

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
