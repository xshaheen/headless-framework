// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Imaging;

/// <summary>
/// Default <see cref="IImageInspector"/> implementation that delegates to a chain of
/// <see cref="IImageInspectorContributor"/> instances resolved from DI.
/// </summary>
/// <remarks>
/// Contributors are iterated in reverse registration order (last-registered wins). Each contributor is given the same
/// seekable stream; the stream is rewound to the beginning after every attempt. If all contributors return
/// <see cref="ImageProcessState.Unsupported"/>, the result is <c>ImageInspectResult.NotSupported()</c>.
/// </remarks>
internal sealed class ImageInspector(IEnumerable<IImageInspectorContributor> contributors) : IImageInspector
{
    private readonly IEnumerable<IImageInspectorContributor> _contributors = contributors.Reverse();

    /// <inheritdoc />
    public async Task<ImageInspectResult> InspectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(stream);

        if (!stream.CanRead)
        {
            return ImageInspectResult.CannotRead();
        }

        if (!stream.CanSeek)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            memoryStream.Seek(0, SeekOrigin.Begin);
            stream = memoryStream;
        }

        foreach (var contributor in _contributors)
        {
            var result = await contributor.TryInspectAsync(stream, cancellationToken).ConfigureAwait(false);

            stream.Seek(0, SeekOrigin.Begin);

            if (result.State is ImageProcessState.Unsupported)
            {
                continue;
            }

            return result;
        }

        return ImageInspectResult.NotSupported();
    }
}
